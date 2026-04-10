using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;
using PC_HealthCheck.Business;
using PC_HealthCheck.Core;
using PC_HealthCheck.DAL;
using PC_HealthCheck.Visualization;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;

namespace PC_HealthCheck.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IHardwareProvider _provider = HardwareProviderFactory.CreateDefault();
    private readonly MonitoringService _monitoring;
    private readonly TestingService _testing = new();
    private readonly DatabaseService _db = new();
    private readonly ReportService _reports = new();
    private readonly DiagnosticService _diagnostics = new();
    private readonly BenchmarkService _benchmarks = new();
    private readonly UserAppSettings _storedSettings;

    private DeviceSnapshot _snapshot = new();
    private double _stressProgress;
    private string _stressStatus = "Тест не запущен";
    private string _statusText = "Инициализация...";
    private string _statusDetails = "";
    private string? _lastReportPath;
    private string _comparisonStatusText = "Базовый снимок не сохранён.";
    private ReportFormat _selectedReportFormat = ReportFormat.Html;
    private DeviceSnapshot? _baselineSnapshot;

    private string _monitoringIntervalText = "1";
    private string _cpuTempWarningText = "75";
    private string _cpuTempCriticalText = "90";
    private string _reportCommentText = "";
    private bool _useDarkTheme;
    private bool _stressConfirmationAccepted;

    private double? _cpuTempSessionMin;
    private double? _cpuTempSessionMax;
    private double _cpuTempSessionSum;
    private int _cpuTempSessionCount;

    private readonly MonitoringLiveChart _liveChart = new();
    private bool _chartShowCpuTemp = true;
    private bool _chartShowCpuLoad = true;
    private bool _benchmarkRunning;
    private DateTime _lastTempAlertUtc = DateTime.MinValue;
    private DateTime _lastRamAlertUtc = DateTime.MinValue;
    private DateTime _lastDiskAlertUtc = DateTime.MinValue;

    private string _utilitiesStatus = "Нажмите «Обновить» в нужном разделе.";

    private const int MaxLiveChartPoints = 720;

    public MainWindowViewModel()
    {
        _storedSettings = UserSettingsStore.Load();
        PullSettingsFromStore();

        _monitoring = new MonitoringService(_provider);
        _monitoring.SnapshotUpdated += MonitoringOnSnapshotUpdated;

        _testing.ProgressChanged += (_, p) =>
            Dispatcher.UIThread.Post(() => StressProgress = p);
        _testing.Completed += (_, r) => Dispatcher.UIThread.Post(async () =>
        {
            StressStatus = r.Status switch
            {
                TestStatus.Completed => "Тест завершен успешно",
                TestStatus.Cancelled => "Тест остановлен",
                TestStatus.Failed => $"Ошибка: {r.Error}",
                _ => r.Status.ToString()
            };
            IsTestRunning = false;
            await _db.SaveTestResultAsync(r);
        });

        _benchmarks.Completed += (_, r) => Dispatcher.UIThread.Post(() =>
        {
            BenchmarkResultLines.Clear();
            BenchmarkResultLines.Add($"CPU (скаляр FP, ~2 с): {r.CpuFpMegaOpsPerSec:F1} млн оп./с");
            BenchmarkResultLines.Add($"RAM (копирование буферов, ~1,5 с): {r.RamBandwidthGbPerSec:F2} ГБ/с");
            if (r.ReferenceCpuMegaOps is { } rc && !string.IsNullOrEmpty(r.ReferenceMatchLabel))
            {
                var pct = rc > 0 ? r.CpuFpMegaOpsPerSec / rc * 100.0 : 0;
                BenchmarkResultLines.Add($"Эталон CPU «{r.ReferenceMatchLabel}»: ~{rc:F1} млн оп./с → ваш результат ≈{pct:F0}% от эталона.");
            }
            else
                BenchmarkResultLines.Add("Эталон CPU: нет совпадения по встроенной таблице моделей.");

            if (r.ReferenceRamGbps is { } rr)
            {
                var p = rr > 0 ? r.RamBandwidthGbPerSec / rr * 100.0 : 0;
                BenchmarkResultLines.Add($"Эталон пропускной способности RAM (оценка под класс CPU): ~{rr:F1} ГБ/с → ваш результат ≈{p:F0}%.");
            }
            else
                BenchmarkResultLines.Add("Эталон RAM: недоступен (не сопоставлена модель CPU).");

            BenchmarkResultLines.Add("Примечание: эталоны ориентировочные; для сравнения используйте ту же версию приложения и закройте фоновые нагрузки.");
            IsBenchmarkRunning = false;
        });

        CpuDetails = new ObservableCollection<string>();
        RamDetails = new ObservableCollection<string>();
        BoardDetails = new ObservableCollection<string>();
        GpuDetails = new ObservableCollection<string>();
        StorageDetails = new ObservableCollection<string>();
        NetworkDetails = new ObservableCollection<string>();
        SmbMemoryDetails = new ObservableCollection<string>();
        PciDetails = new ObservableCollection<string>();
        PeripheralDetails = new ObservableCollection<string>();
        PrinterDetails = new ObservableCollection<string>();
        SensorDetails = new ObservableCollection<string>();
        DiagnosticDetails = new ObservableCollection<string>();
        HardwareSensorDetails = new ObservableCollection<string>();
        RamTimingDetails = new ObservableCollection<string>();
        CoreClockDetails = new ObservableCollection<string>();
        CoreVoltageDetails = new ObservableCollection<string>();
        VrmChipsetDetails = new ObservableCollection<string>();
        FanSensorDetails = new ObservableCollection<string>();
        StorageTempDetails = new ObservableCollection<string>();
        BenchmarkResultLines = new ObservableCollection<string>();
        ProcessLines = new ObservableCollection<string>();
        NetworkStatLines = new ObservableCollection<string>();
        StartupLines = new ObservableCollection<string>();
        AlertHistoryLines = new ObservableCollection<string>();

        StartCpuStressCommand = new AsyncRelayCommand(StartCpuStressAsync);
        StartRamStressCommand = new AsyncRelayCommand(StartRamStressAsync);
        StartGpuStressCommand = new AsyncRelayCommand(StartGpuStressAsync);
        StartDiskStressCommand = new AsyncRelayCommand(StartDiskStressAsync);
        StopStressCommand = new RelayCommand(() => _testing.Stop());
        GenerateReportCommand = new RelayCommand(GenerateReport);
        SaveBaselineSnapshotCommand = new RelayCommand(SaveBaselineSnapshot);
        GenerateComparisonReportCommand = new RelayCommand(GenerateComparisonReport);
        ApplySettingsCommand = new AsyncRelayCommand(ApplySettingsAsync);
        SnapshotCommand = new AsyncRelayCommand(TakeSnapshotAsync);
        RunDiagnosticsCommand = new RelayCommand(RunDiagnostics);
        RunBenchmarkCommand = new AsyncRelayCommand(RunBenchmarkAsync);
        RefreshProcessesCommand = new RelayCommand(RefreshProcesses);
        RefreshNetworkStatsCommand = new RelayCommand(RefreshNetworkStats);
        RefreshStartupCommand = new RelayCommand(RefreshStartup);
        CleanupTempCommand = new RelayCommand(CleanupTemp);

        ApplyThemeFromSettings();

        _ = InitializeAsync();
    }

    private void PullSettingsFromStore()
    {
        _monitoringIntervalText = Math.Clamp(_storedSettings.MonitoringIntervalSeconds, 1, 3600).ToString();
        _cpuTempWarningText = Math.Clamp(_storedSettings.CpuTempWarningCelsius, 30, 120).ToString();
        _cpuTempCriticalText = Math.Clamp(_storedSettings.CpuTempCriticalCelsius, 40, 125).ToString();
        _reportCommentText = _storedSettings.ReportComment ?? "";
        _useDarkTheme = _storedSettings.UseDarkTheme;
        _stressConfirmationAccepted = _storedSettings.StressTestConfirmationAccepted;
    }

    private void PushSettingsToStore()
    {
        _storedSettings.MonitoringIntervalSeconds = ParseIntUi(MonitoringIntervalText, 1, 1, 3600);
        _storedSettings.CpuTempWarningCelsius = ParseIntUi(CpuTempWarningText, 75, 30, 120);
        _storedSettings.CpuTempCriticalCelsius = ParseIntUi(CpuTempCriticalText, 90, 40, 125);
        if (_storedSettings.CpuTempCriticalCelsius < _storedSettings.CpuTempWarningCelsius)
            _storedSettings.CpuTempCriticalCelsius = _storedSettings.CpuTempWarningCelsius;
        _storedSettings.ReportComment = ReportCommentText ?? "";
        _storedSettings.UseDarkTheme = UseDarkTheme;
        _storedSettings.StressTestConfirmationAccepted = StressConfirmationAccepted;
    }

    private static int ParseIntUi(string? text, int fallback, int min, int max)
    {
        if (int.TryParse(text?.Trim(), out var v))
            return Math.Clamp(v, min, max);
        return fallback;
    }

    public ObservableCollection<string> CpuDetails { get; }
    public ObservableCollection<string> RamDetails { get; }
    public ObservableCollection<string> BoardDetails { get; }
    public ObservableCollection<string> GpuDetails { get; }
    public ObservableCollection<string> StorageDetails { get; }
    public ObservableCollection<string> NetworkDetails { get; }
    public ObservableCollection<string> SmbMemoryDetails { get; }
    public ObservableCollection<string> PciDetails { get; }
    public ObservableCollection<string> PeripheralDetails { get; }
    public ObservableCollection<string> PrinterDetails { get; }
    public ObservableCollection<string> SensorDetails { get; }
    public ObservableCollection<string> DiagnosticDetails { get; }
    public ObservableCollection<string> HardwareSensorDetails { get; }

    public ObservableCollection<string> RamTimingDetails { get; }

    public ObservableCollection<string> CoreClockDetails { get; }

    public ObservableCollection<string> CoreVoltageDetails { get; }

    public ObservableCollection<string> VrmChipsetDetails { get; }

    public ObservableCollection<string> FanSensorDetails { get; }

    public ObservableCollection<string> StorageTempDetails { get; }

    public ObservableCollection<string> BenchmarkResultLines { get; }

    public ObservableCollection<string> ProcessLines { get; }

    public ObservableCollection<string> NetworkStatLines { get; }

    public ObservableCollection<string> StartupLines { get; }
    public ObservableCollection<string> AlertHistoryLines { get; }

    public PlotModel MonitoringPlotModel => _liveChart.Model;

    public bool ChartShowCpuTemp
    {
        get => _chartShowCpuTemp;
        set
        {
            if (SetProperty(ref _chartShowCpuTemp, value))
                _liveChart.SetSeriesVisibility(_chartShowCpuTemp, _chartShowCpuLoad);
        }
    }

    public bool ChartShowCpuLoad
    {
        get => _chartShowCpuLoad;
        set
        {
            if (SetProperty(ref _chartShowCpuLoad, value))
                _liveChart.SetSeriesVisibility(_chartShowCpuTemp, _chartShowCpuLoad);
        }
    }

    public bool IsTestRunning { get; private set; }

    public bool IsBenchmarkRunning
    {
        get => _benchmarkRunning;
        private set => SetProperty(ref _benchmarkRunning, value);
    }

    public double StressProgress
    {
        get => _stressProgress;
        set => SetProperty(ref _stressProgress, value);
    }

    public string StressStatus
    {
        get => _stressStatus;
        set => SetProperty(ref _stressStatus, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string StatusDetails
    {
        get => _statusDetails;
        set => SetProperty(ref _statusDetails, value);
    }

    public string CpuHeader => _snapshot.CpuName;
    public string CpuTempText => TemperatureStatusLine(_snapshot.CpuTemperatureC);
    public string CpuLoadText => $"CPU загрузка: {(_snapshot.CpuLoadPercent?.ToString("F0") ?? "N/A")} %";
    public string RamHeader => $"RAM: {_snapshot.RamType}, {(BytesToGb(_snapshot.TotalRamBytes)):F1} GB";
    public string RamUsageText => $"RAM использование: {_snapshot.RamUsagePercent:F1}%";
    public DateTime LastUpdateLocal => _monitoring.LastUpdateLocal;

    public string CpuTempSessionSummary => _cpuTempSessionCount == 0
        ? "За сессию нет отсчётов температуры CPU (данные появятся после опроса датчиков)."
        : $"CPU °C за сессию: мин {_cpuTempSessionMin:F1}, макс {_cpuTempSessionMax:F1}, средн. {_cpuTempSessionSum / _cpuTempSessionCount:F1} ({_cpuTempSessionCount} отсчётов)";

    public string MonitoringNote =>
        "График OxyPlot: температура и загрузка CPU; полосы — зоны по порогам из «Настройки». Ниже — сгруппированные списки LHM (ядра, VRM/PCH, вентиляторы, температура накопителей) и полный перечень сенсоров.";

    private string TemperatureStatusLine(double? tempC)
    {
        var baseText = $"CPU температура: {(tempC?.ToString("F1") ?? "N/A")} °C";
        if (tempC is null)
            return baseText;
        var warn = _storedSettings.CpuTempWarningCelsius;
        var crit = Math.Max(_storedSettings.CpuTempCriticalCelsius, warn);
        if (tempC >= crit)
            return baseText + " — опасность (≥ крит. порога)";
        if (tempC >= warn)
            return baseText + " — предупреждение (≥ порога)";
        return baseText + " — норма";
    }

    public ReportFormat SelectedReportFormat
    {
        get => _selectedReportFormat;
        set => SetProperty(ref _selectedReportFormat, value);
    }

    public IReadOnlyList<ReportFormat> ReportFormatOptions { get; } =
        new[] { ReportFormat.Txt, ReportFormat.Html, ReportFormat.Csv, ReportFormat.Json };

    public string? LastReportPath
    {
        get => _lastReportPath;
        set => SetProperty(ref _lastReportPath, value);
    }

    public string ComparisonStatusText
    {
        get => _comparisonStatusText;
        set => SetProperty(ref _comparisonStatusText, value);
    }

    public string MonitoringIntervalText
    {
        get => _monitoringIntervalText;
        set => SetProperty(ref _monitoringIntervalText, value);
    }

    public string CpuTempWarningText
    {
        get => _cpuTempWarningText;
        set => SetProperty(ref _cpuTempWarningText, value);
    }

    public string CpuTempCriticalText
    {
        get => _cpuTempCriticalText;
        set => SetProperty(ref _cpuTempCriticalText, value);
    }

    public string ReportCommentText
    {
        get => _reportCommentText;
        set => SetProperty(ref _reportCommentText, value);
    }

    public bool UseDarkTheme
    {
        get => _useDarkTheme;
        set => SetProperty(ref _useDarkTheme, value);
    }

    public bool StressConfirmationAccepted
    {
        get => _stressConfirmationAccepted;
        set => SetProperty(ref _stressConfirmationAccepted, value);
    }

    public IAsyncRelayCommand StartCpuStressCommand { get; }
    public IAsyncRelayCommand StartRamStressCommand { get; }
    public IAsyncRelayCommand StartGpuStressCommand { get; }
    public IAsyncRelayCommand StartDiskStressCommand { get; }
    public IRelayCommand StopStressCommand { get; }
    public IRelayCommand GenerateReportCommand { get; }
    public IRelayCommand SaveBaselineSnapshotCommand { get; }
    public IRelayCommand GenerateComparisonReportCommand { get; }
    public IAsyncRelayCommand ApplySettingsCommand { get; }
    public IAsyncRelayCommand SnapshotCommand { get; }
    public IRelayCommand RunDiagnosticsCommand { get; }

    public IAsyncRelayCommand RunBenchmarkCommand { get; }

    public IRelayCommand RefreshProcessesCommand { get; }

    public IRelayCommand RefreshNetworkStatsCommand { get; }

    public IRelayCommand RefreshStartupCommand { get; }

    public IRelayCommand CleanupTempCommand { get; }

    public string UtilitiesStatus
    {
        get => _utilitiesStatus;
        private set => SetProperty(ref _utilitiesStatus, value);
    }

    private void ApplyThemeFromSettings()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = _storedSettings.UseDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void ResetSessionTemperatureStats()
    {
        _cpuTempSessionMin = null;
        _cpuTempSessionMax = null;
        _cpuTempSessionSum = 0;
        _cpuTempSessionCount = 0;
        _liveChart.Reset();
        OnPropertyChanged(nameof(CpuTempSessionSummary));
    }

    private async Task InitializeAsync()
    {
        var ok = await _provider.InitializeAsync();
        if (!ok)
        {
            StatusText = "Ошибка";
            StatusDetails = "Не удалось инициализировать датчики";
            return;
        }

        ResetSessionTemperatureStats();
        var sec = Math.Max(1, _storedSettings.MonitoringIntervalSeconds);
        await _monitoring.StartAsync(TimeSpan.FromSeconds(sec));
        StatusText = "Мониторинг активен";
        var limitedNote = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ""
            : " Режим Linux/macOS: базовый мониторинг без WMI/LHM-датчиков.";
        StatusDetails = $"Интервал опроса: {sec} с. Настройки можно изменить на вкладке «Настройки».{limitedNote}";
    }

    private async Task ApplySettingsAsync()
    {
        PushSettingsToStore();
        UserSettingsStore.Save(_storedSettings);
        ApplyThemeFromSettings();

        MonitoringIntervalText = _storedSettings.MonitoringIntervalSeconds.ToString();
        CpuTempWarningText = _storedSettings.CpuTempWarningCelsius.ToString();
        CpuTempCriticalText = _storedSettings.CpuTempCriticalCelsius.ToString();

        if (!_provider.IsInitialized)
        {
            StatusDetails = "Настройки сохранены. Мониторинг недоступен до успешной инициализации.";
            return;
        }

        ResetSessionTemperatureStats();
        var sec = _storedSettings.MonitoringIntervalSeconds;
        await _monitoring.StartAsync(TimeSpan.FromSeconds(sec));
        StatusText = "Мониторинг активен";
        StatusDetails = $"Настройки применены. Интервал: {sec} с; пороги CPU: предупр. {_storedSettings.CpuTempWarningCelsius}°C, крит. {_storedSettings.CpuTempCriticalCelsius}°C.";

        OnPropertyChanged(nameof(CpuTempText));
    }

    private async Task StartCpuStressAsync()
    {
        if (IsTestRunning) return;
        if (!StressConfirmationAccepted)
        {
            StressStatus = "Подтвердите запуск стресс-тестов на вкладке «Настройки».";
            return;
        }
        IsTestRunning = true;
        StressProgress = 0;
        StressStatus = "CPU тест выполняется...";
        var started = await _testing.StartCpuTestAsync(60);
        if (!started)
        {
            IsTestRunning = false;
            StressStatus = "Не удалось запустить CPU тест";
        }
    }

    private async Task StartRamStressAsync()
    {
        if (IsTestRunning) return;
        if (!StressConfirmationAccepted)
        {
            StressStatus = "Подтвердите запуск стресс-тестов на вкладке «Настройки».";
            return;
        }
        IsTestRunning = true;
        StressProgress = 0;
        StressStatus = "RAM тест выполняется...";
        var started = await _testing.StartRamTestAsync(60);
        if (!started)
        {
            IsTestRunning = false;
            StressStatus = "Не удалось запустить RAM тест";
        }
    }

    private async Task StartGpuStressAsync()
    {
        if (IsTestRunning) return;
        if (!StressConfirmationAccepted)
        {
            StressStatus = "Подтвердите запуск стресс-тестов на вкладке «Настройки».";
            return;
        }
        IsTestRunning = true;
        StressProgress = 0;
        StressStatus = "GPU тест выполняется...";
        var started = await _testing.StartGpuTestAsync(60);
        if (!started)
        {
            IsTestRunning = false;
            StressStatus = "Не удалось запустить GPU тест";
        }
    }

    private async Task StartDiskStressAsync()
    {
        if (IsTestRunning) return;
        if (!StressConfirmationAccepted)
        {
            StressStatus = "Подтвердите запуск стресс-тестов на вкладке «Настройки».";
            return;
        }
        IsTestRunning = true;
        StressProgress = 0;
        StressStatus = "Disk тест выполняется...";
        var started = await _testing.StartDiskTestAsync(60);
        if (!started)
        {
            IsTestRunning = false;
            StressStatus = "Не удалось запустить Disk тест";
        }
    }

    private async Task TakeSnapshotAsync()
    {
        var s = await _provider.ReadSnapshotAsync();
        UpdateView(s);
    }

    private void MonitoringOnSnapshotUpdated(object? sender, DeviceSnapshot s)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            UpdateView(s);
            if (IsTestRunning && s.CpuTemperatureC is { } t &&
                t >= Math.Max(_storedSettings.CpuTempCriticalCelsius, _storedSettings.CpuTempWarningCelsius))
            {
                _testing.Stop();
                StressStatus = $"Тест аварийно остановлен: CPU {t:F1}°C достиг критического порога.";
                AddAlert("CRITICAL", $"CPU {t:F1}°C достиг критического порога, тест остановлен автоматически.");
            }
            await _db.SaveSnapshotAsync(s);
        });
    }

    private void UpdateSessionTemperatureStats(DeviceSnapshot s)
    {
        if (s.CpuTemperatureC is not { } t) return;
        _cpuTempSessionMin = _cpuTempSessionMin is { } mn ? Math.Min(mn, t) : t;
        _cpuTempSessionMax = _cpuTempSessionMax is { } mx ? Math.Max(mx, t) : t;
        _cpuTempSessionSum += t;
        _cpuTempSessionCount++;
    }

    private void RunDiagnostics()
    {
        PushSettingsToStore();
        DiagnosticDetails.Clear();
        foreach (var line in _diagnostics.Analyze(_snapshot, _storedSettings))
            DiagnosticDetails.Add(line);
    }

    private void UpdateView(DeviceSnapshot s)
    {
        _snapshot = s;
        UpdateSessionTemperatureStats(s);

        StatusDetails = $"Мониторинг обновлен: {LastUpdateLocal:HH:mm:ss} • Обновлений: {_monitoring.UpdateCount} • Аптайм: {(DateTime.Now - _monitoring.StartedLocal):hh\\:mm\\:ss}";

        CpuDetails.Clear();
        CpuDetails.Add($"Name: {s.CpuName}");
        CpuDetails.Add($"Manufacturer: {s.CpuManufacturer}");
        CpuDetails.Add($"Cores: {s.CpuCores}");
        CpuDetails.Add($"Threads: {s.CpuThreads}");
        CpuDetails.Add($"Max clock: {s.CpuMaxClockMHz} MHz");
        CpuDetails.Add($"Family / Model / Stepping: {s.CpuFamily} / {s.CpuModel} / {s.CpuStepping}");
        if (!string.IsNullOrEmpty(s.CpuSignatureRaw))
            CpuDetails.Add($"Сигнатура: {s.CpuSignatureRaw}");
        if (!string.IsNullOrEmpty(s.CpuMicroarchitectureHint))
            CpuDetails.Add($"Микроархитектура: {s.CpuMicroarchitectureHint}");
        CpuDetails.Add($"Инструкции: {s.CpuInstructionSets}");

        RamDetails.Clear();
        RamDetails.Add($"Type: {s.RamType}");
        RamDetails.Add($"Total: {BytesToGb(s.TotalRamBytes):F2} GB");
        RamDetails.Add($"Free: {BytesToGb(s.FreeRamBytes):F2} GB");
        RamDetails.Add($"Usage: {s.RamUsagePercent:F1}%");
        RamDetails.Add($"Modules: {s.MemoryModules.Count}");
        foreach (var m in s.MemoryModules)
        {
            var cfg = m.ConfiguredClockSpeedMHz > 0 ? $", сконфиг. {m.ConfiguredClockSpeedMHz} МГц" : "";
            var uv = m.ConfiguredVoltageMilliVolts > 0 ? $", U≈{m.ConfiguredVoltageMilliVolts} мВ" : "";
            RamDetails.Add(
                $"- {m.DeviceLocator}/{m.BankLabel}: {BytesToGb(m.CapacityBytes):F1} GB @ {m.SpeedMHz} МГц{cfg}{uv}, {m.FormFactor}, {m.Manufacturer}, {m.PartNumber}");
        }

        RamTimingDetails.Clear();
        if (s.RamTimingSummaryLines.Count == 0)
            RamTimingDetails.Add("(Данные появятся после успешной инициализации WMI.)");
        else
            foreach (var line in s.RamTimingSummaryLines)
                RamTimingDetails.Add(line);

        SmbMemoryDetails.Clear();
        foreach (var a in s.MemoryArrays)
            SmbMemoryDetails.Add($"Массив: {a.Use}, слотов {a.MemoryDevices}, макс. {BytesToGb(a.MaxCapacityBytes):F0} GB, ECC: {a.ErrorCorrection}");
        if (s.MemorySlots.Count == 0)
            SmbMemoryDetails.Add("(Win32_MemoryDevice: нет данных или класс недоступен в WMI)");
        else
            foreach (var x in s.MemorySlots)
            {
                var cap = x.CapacityBytes.HasValue ? $"{BytesToGb(x.CapacityBytes.Value):F1} GB" : "—";
                SmbMemoryDetails.Add($"{x.DeviceLocator}/{x.BankLabel}: {cap}, {x.SpeedMHz} MHz, {x.FormFactor}, {x.MemoryType}");
            }

        BoardDetails.Clear();
        BoardDetails.Add($"Motherboard: {s.MotherboardManufacturer} {s.MotherboardModel}");
        BoardDetails.Add($"BIOS: {s.BiosVersion}");
        BoardDetails.Add($"BIOS Date: {s.BiosDate}");

        GpuDetails.Clear();
        foreach (var g in s.Gpus)
            GpuDetails.Add($"{g.Name} | {g.Manufacturer} | Driver {g.DriverVersion} | VRAM {BytesToGb(g.VramBytes):F1} GB");

        StorageDetails.Clear();
        foreach (var d in s.PhysicalDisks)
        {
            var smart = d.SmartPredictFailure is { } b ? $"SMART: {(b ? "FAIL" : "OK")} {d.SmartReason}" : "SMART: н/д";
            StorageDetails.Add($"[Физ.] {d.Model} ({d.InterfaceType}) {BytesToGb(d.SizeBytes):F0} GB — {smart}");
            foreach (var a in d.SmartAttributes.Take(12))
                StorageDetails.Add($"   {a.Name}: cur={a.Current} worst={a.Worst} raw={a.RawValue}");
        }

        foreach (var l in s.LogicalDisks)
        {
            var pct = l.SizeBytes > 0 ? (1.0 - (double)l.FreeBytes / l.SizeBytes) * 100.0 : 0;
            StorageDetails.Add($"[Лог.] {l.DeviceId} {l.VolumeName} ({l.FileSystem}) {BytesToGb(l.SizeBytes):F1} GB, занято {pct:F0}%");
        }

        NetworkDetails.Clear();
        foreach (var n in s.NetworkAdapters)
        {
            var mb = n.SpeedBitsPerSec > 0 ? $"{n.SpeedBitsPerSec / 1_000_000} Mbps" : "?";
            NetworkDetails.Add($"{n.Name}: {n.MacAddress}, {mb}, {(n.NetEnabled ? "вкл" : "выкл")}, {n.NetConnectionId}");
        }

        PciDetails.Clear();
        foreach (var p in s.PciDevices.Take(100))
            PciDetails.Add($"{p.Name} [{p.PnpClass}]");

        PeripheralDetails.Clear();
        foreach (var p in s.Peripherals)
            PeripheralDetails.Add($"[{p.Category}] {p.Name}");

        PrinterDetails.Clear();
        foreach (var p in s.Printers)
            PrinterDetails.Add($"{p.Name} ({p.PortName}){(p.Default ? " *" : "")}");

        SensorDetails.Clear();
        foreach (var sr in s.CpuSensors.OrderBy(x => x.Kind).ThenBy(x => x.Name))
            SensorDetails.Add($"{sr.Kind}: {sr.Name} = {sr.Value:F1}{sr.Unit}");

        HardwareSensorDetails.Clear();
        foreach (var sr in s.AllHardwareSensors.OrderBy(x => x.HardwareGroup).ThenBy(x => x.Kind).ThenBy(x => x.Name))
            HardwareSensorDetails.Add($"[{sr.HardwareGroup}] {sr.Kind}: {sr.Name} = {sr.Value:F2} {sr.Unit}".TrimEnd());

        static void FillSensorLines(ObservableCollection<string> target, IReadOnlyList<SensorReading> src)
        {
            target.Clear();
            if (src.Count == 0)
            {
                target.Add("(нет данных — проверьте LHM и драйверы материнской платы / накопителей)");
                return;
            }

            foreach (var sr in src.OrderBy(x => x.Name))
                target.Add($"{sr.Name} = {sr.Value:F2} {sr.Unit}".TrimEnd());
        }

        FillSensorLines(CoreClockDetails, s.CpuPerCoreClocks);
        FillSensorLines(CoreVoltageDetails, s.CpuPerCoreVoltages);
        FillSensorLines(VrmChipsetDetails, s.VrmChipsetSensors);
        FillSensorLines(FanSensorDetails, s.FanSensors);
        FillSensorLines(StorageTempDetails, s.StorageTemperatureSensors);

        _liveChart.Append(
            s.CpuTemperatureC,
            s.CpuLoadPercent,
            ChartShowCpuTemp,
            ChartShowCpuLoad,
            _storedSettings.CpuTempWarningCelsius,
            Math.Max(_storedSettings.CpuTempCriticalCelsius, _storedSettings.CpuTempWarningCelsius),
            MaxLiveChartPoints);

        OnPropertyChanged(nameof(CpuHeader));
        OnPropertyChanged(nameof(CpuTempText));
        OnPropertyChanged(nameof(CpuLoadText));
        OnPropertyChanged(nameof(RamHeader));
        OnPropertyChanged(nameof(RamUsageText));
        OnPropertyChanged(nameof(LastUpdateLocal));
        OnPropertyChanged(nameof(CpuTempSessionSummary));
        EvaluateAlerts(s);
    }

    private void EvaluateAlerts(DeviceSnapshot s)
    {
        var now = DateTime.Now;
        if (s.CpuTemperatureC is { } t && t >= _storedSettings.CpuTempWarningCelsius && now - _lastTempAlertUtc > TimeSpan.FromSeconds(30))
        {
            _lastTempAlertUtc = now;
            AddAlert(t >= _storedSettings.CpuTempCriticalCelsius ? "CRITICAL" : "WARN", $"CPU температура {t:F1}°C.");
        }

        if (s.RamUsagePercent >= 90 && now - _lastRamAlertUtc > TimeSpan.FromSeconds(45))
        {
            _lastRamAlertUtc = now;
            AddAlert("WARN", $"Высокая загрузка RAM: {s.RamUsagePercent:F0}%.");
        }

        var diskCritical = s.LogicalDisks.FirstOrDefault(x => x.SizeBytes > 0 && (1.0 - (double)x.FreeBytes / x.SizeBytes) >= 0.9);
        if (diskCritical is not null && now - _lastDiskAlertUtc > TimeSpan.FromSeconds(60))
        {
            _lastDiskAlertUtc = now;
            var used = (1.0 - (double)diskCritical.FreeBytes / diskCritical.SizeBytes) * 100.0;
            AddAlert("WARN", $"Диск {diskCritical.DeviceId} заполнен на {used:F0}%.");
        }
    }

    private void AddAlert(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        AlertHistoryLines.Insert(0, line);
        while (AlertHistoryLines.Count > 500)
            AlertHistoryLines.RemoveAt(AlertHistoryLines.Count - 1);
        StatusText = level == "CRITICAL" ? "Критическое предупреждение" : "Предупреждение";
        StatusDetails = message;
    }

    private async Task RunBenchmarkAsync()
    {
        if (IsBenchmarkRunning || IsTestRunning) return;
        IsBenchmarkRunning = true;
        BenchmarkResultLines.Clear();
        BenchmarkResultLines.Add("Выполняется быстрый бенчмарк (~4 с)...");
        try
        {
            var ok = await _benchmarks.RunQuickSuiteAsync(_snapshot);
            if (!ok)
            {
                BenchmarkResultLines.Clear();
                BenchmarkResultLines.Add("Не удалось запустить (бенчмарк уже выполняется).");
                IsBenchmarkRunning = false;
            }
        }
        catch (Exception ex)
        {
            BenchmarkResultLines.Clear();
            BenchmarkResultLines.Add($"Ошибка бенчмарка: {ex.Message}");
            IsBenchmarkRunning = false;
        }
    }

    private void RefreshProcesses()
    {
        ProcessLines.Clear();
        foreach (var p in UtilitiesService.GetTopProcessesByWorkingSet(120))
        {
            var mb = p.WorkingSetBytes / (1024.0 * 1024.0);
            ProcessLines.Add($"PID {p.ProcessId,6}  {mb:F0} МБ  потоков={p.ThreadCount,-3}  {p.Name}");
        }
    }

    private void RefreshNetworkStats()
    {
        NetworkStatLines.Clear();
        foreach (var n in UtilitiesService.GetNetworkCounters())
        {
            NetworkStatLines.Add(
                $"{n.Name}: отправлено {n.BytesSent:N0} Б, получено {n.BytesReceived:N0} Б (с загрузки ОС) — {n.Description}");
        }
    }

    private void RefreshStartup()
    {
        StartupLines.Clear();
        foreach (var e in UtilitiesService.GetStartupEntries().OrderBy(x => x.Location).ThenBy(x => x.Name))
            StartupLines.Add($"[{e.Location}] {e.Name}: {e.Command}");
    }

    private void CleanupTemp()
    {
        var r = UtilitiesService.CleanupOldTempFiles(7);
        UtilitiesStatus = r.Detail;
    }

    private void GenerateReport()
    {
        PushSettingsToStore();
        UserSettingsStore.Save(_storedSettings);
        var comment = string.IsNullOrWhiteSpace(ReportCommentText) ? null : ReportCommentText.Trim();
        var content = _reports.Build(_snapshot, SelectedReportFormat, comment);
        LastReportPath = _reports.Save(content, SelectedReportFormat);
    }

    private void SaveBaselineSnapshot()
    {
        _baselineSnapshot = CloneSnapshot(_snapshot);
        ComparisonStatusText = $"Базовый снимок сохранён: {_baselineSnapshot.TimestampLocal:yyyy-MM-dd HH:mm:ss}";
    }

    private void GenerateComparisonReport()
    {
        if (_baselineSnapshot is null)
        {
            ComparisonStatusText = "Сначала сохраните базовый снимок («До»).";
            return;
        }

        PushSettingsToStore();
        UserSettingsStore.Save(_storedSettings);
        var comment = string.IsNullOrWhiteSpace(ReportCommentText) ? null : ReportCommentText.Trim();
        var content = _reports.BuildComparison(
            _baselineSnapshot,
            _snapshot,
            SelectedReportFormat,
            "До",
            "После",
            comment);
        LastReportPath = _reports.Save(content, SelectedReportFormat);
        ComparisonStatusText = $"Сравнительный отчет сформирован: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
    }

    private static DeviceSnapshot CloneSnapshot(DeviceSnapshot s)
    {
        return new DeviceSnapshot
        {
            TimestampUtc = s.TimestampUtc,
            CpuName = s.CpuName,
            CpuManufacturer = s.CpuManufacturer,
            CpuCores = s.CpuCores,
            CpuThreads = s.CpuThreads,
            CpuMaxClockMHz = s.CpuMaxClockMHz,
            CpuFamily = s.CpuFamily,
            CpuModel = s.CpuModel,
            CpuStepping = s.CpuStepping,
            CpuSignatureRaw = s.CpuSignatureRaw,
            CpuMicroarchitectureHint = s.CpuMicroarchitectureHint,
            CpuInstructionSets = s.CpuInstructionSets,
            CpuTemperatureC = s.CpuTemperatureC,
            CpuLoadPercent = s.CpuLoadPercent,
            TotalRamBytes = s.TotalRamBytes,
            FreeRamBytes = s.FreeRamBytes,
            RamType = s.RamType,
            MotherboardModel = s.MotherboardModel,
            MotherboardManufacturer = s.MotherboardManufacturer,
            BiosVersion = s.BiosVersion,
            BiosDate = s.BiosDate,
            Gpus = s.Gpus.Select(x => new GpuInfo
            {
                Name = x.Name,
                Manufacturer = x.Manufacturer,
                DriverVersion = x.DriverVersion,
                VramBytes = x.VramBytes
            }).ToList(),
            LogicalDisks = s.LogicalDisks.Select(x => new LogicalDiskInfo
            {
                DeviceId = x.DeviceId,
                VolumeName = x.VolumeName,
                FileSystem = x.FileSystem,
                SizeBytes = x.SizeBytes,
                FreeBytes = x.FreeBytes
            }).ToList(),
            NetworkAdapters = s.NetworkAdapters.Select(x => new NetworkAdapterInfo
            {
                Name = x.Name,
                Manufacturer = x.Manufacturer,
                MacAddress = x.MacAddress,
                NetEnabled = x.NetEnabled,
                NetConnectionId = x.NetConnectionId,
                SpeedBitsPerSec = x.SpeedBitsPerSec,
                AdapterType = x.AdapterType
            }).ToList()
        };
    }

    private static double BytesToGb(long bytes) => bytes / 1024d / 1024d / 1024d;

    public void Dispose()
    {
        _monitoring.Dispose();
        _testing.Dispose();
        _db.Dispose();
        _provider.Dispose();
    }
}
