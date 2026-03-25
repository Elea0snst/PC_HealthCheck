using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using PC_HealthCheck.Business;
using PC_HealthCheck.Core;
using PC_HealthCheck.DAL;
using System.Collections.ObjectModel;

namespace PC_HealthCheck.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly WindowsHardwareProvider _provider = new();
    private readonly MonitoringService _monitoring;
    private readonly TestingService _testing = new();
    private readonly DatabaseService _db = new();
    private readonly ReportService _reports = new();
    private readonly DiagnosticService _diagnostics = new();
    private readonly UserAppSettings _storedSettings;

    private DeviceSnapshot _snapshot = new();
    private double _stressProgress;
    private string _stressStatus = "Тест не запущен";
    private string _statusText = "Инициализация...";
    private string _statusDetails = "";
    private string? _lastReportPath;
    private ReportFormat _selectedReportFormat = ReportFormat.Html;

    private string _monitoringIntervalText = "1";
    private string _cpuTempWarningText = "75";
    private string _cpuTempCriticalText = "90";
    private string _reportCommentText = "";
    private bool _useDarkTheme;

    private double? _cpuTempSessionMin;
    private double? _cpuTempSessionMax;
    private double _cpuTempSessionSum;
    private int _cpuTempSessionCount;

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

        StartCpuStressCommand = new AsyncRelayCommand(StartCpuStressAsync);
        StartRamStressCommand = new AsyncRelayCommand(StartRamStressAsync);
        StopStressCommand = new RelayCommand(() => _testing.Stop());
        GenerateReportCommand = new RelayCommand(GenerateReport);
        ApplySettingsCommand = new AsyncRelayCommand(ApplySettingsAsync);
        SnapshotCommand = new AsyncRelayCommand(TakeSnapshotAsync);
        RunDiagnosticsCommand = new RelayCommand(RunDiagnostics);

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

    public bool IsTestRunning { get; private set; }

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
        "Графики в реальном времени (OxyPlot), выбор параметров и цветовая индикация по ТЗ 2.1.2 — в следующей итерации. Здесь — сводка мин./макс./средн. за текущую сессию мониторинга.";

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

    public IAsyncRelayCommand StartCpuStressCommand { get; }
    public IAsyncRelayCommand StartRamStressCommand { get; }
    public IRelayCommand StopStressCommand { get; }
    public IRelayCommand GenerateReportCommand { get; }
    public IAsyncRelayCommand ApplySettingsCommand { get; }
    public IAsyncRelayCommand SnapshotCommand { get; }
    public IRelayCommand RunDiagnosticsCommand { get; }

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
        StatusDetails = $"Интервал опроса: {sec} с. Настройки можно изменить на вкладке «Настройки».";
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
            RamDetails.Add(
                $"- {m.DeviceLocator}/{m.BankLabel}: {BytesToGb(m.CapacityBytes):F1} GB @ {m.SpeedMHz} MHz, {m.FormFactor}, {m.Manufacturer}, {m.PartNumber}");

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

        OnPropertyChanged(nameof(CpuHeader));
        OnPropertyChanged(nameof(CpuTempText));
        OnPropertyChanged(nameof(CpuLoadText));
        OnPropertyChanged(nameof(RamHeader));
        OnPropertyChanged(nameof(RamUsageText));
        OnPropertyChanged(nameof(LastUpdateLocal));
        OnPropertyChanged(nameof(CpuTempSessionSummary));
    }

    private void GenerateReport()
    {
        PushSettingsToStore();
        UserSettingsStore.Save(_storedSettings);
        var comment = string.IsNullOrWhiteSpace(ReportCommentText) ? null : ReportCommentText.Trim();
        var content = _reports.Build(_snapshot, SelectedReportFormat, comment);
        LastReportPath = _reports.Save(content, SelectedReportFormat);
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
