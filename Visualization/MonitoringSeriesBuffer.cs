namespace PC_HealthCheck.Visualization;

/// <summary>Буфер точек для графика мониторинга (без OxyPlot).</summary>
public sealed class MonitoringSeriesBuffer
{
    private const int DefaultMaxPoints = 720;
    private DateTime _epochUtc = DateTime.UtcNow;
    private string _title = "Мониторинг";

    private readonly SeriesChannel _cpuTemp = new("Температура CPU", "#4682B4", "Temp");
    private readonly SeriesChannel _cpuLoad = new("Загрузка CPU", "#FF8C00", "Percent");
    private readonly SeriesChannel _gpuTemp = new("Температура GPU", "#9370DB", "Temp");
    private readonly SeriesChannel _gpuLoad = new("Загрузка GPU", "#228B22", "Percent");
    private readonly SeriesChannel _ramUsage = new("Загрузка RAM", "#708090", "Percent");
    private readonly SeriesChannel _storageTemp = new("Темп. накопителя", "#8B4513", "Temp");
    private readonly SeriesChannel _cpuPower = new("Мощность CPU", "#5F9EA0", "Percent");

    public void Reset()
    {
        _epochUtc = DateTime.UtcNow;
        foreach (var ch in AllChannels())
            ch.Clear();
    }

    public void SetTitle(string title) => _title = title;

    public void Append(
        double? cpuTempC,
        double? cpuLoadPct,
        double? gpuTempC,
        double? gpuLoadPct,
        double ramUsagePct,
        double? storageTempC,
        double? cpuPowerW,
        bool showCpuTemp,
        bool showCpuLoad,
        bool showGpuTemp,
        bool showGpuLoad,
        bool showRamUsage,
        bool showStorageTemp,
        bool showCpuPower,
        int maxPoints = DefaultMaxPoints)
    {
        var x = (DateTime.UtcNow - _epochUtc).TotalSeconds;
        _cpuTemp.Add(showCpuTemp, cpuTempC, x, maxPoints);
        _cpuLoad.Add(showCpuLoad, cpuLoadPct, x, maxPoints);
        _gpuTemp.Add(showGpuTemp, gpuTempC, x, maxPoints);
        _gpuLoad.Add(showGpuLoad, gpuLoadPct, x, maxPoints);
        _ramUsage.Add(showRamUsage, ramUsagePct, x, maxPoints);
        _storageTemp.Add(showStorageTemp, storageTempC, x, maxPoints);
        _cpuPower.Add(showCpuPower, cpuPowerW, x, maxPoints);
    }

    public void SetVisibility(
        bool showCpuTemp,
        bool showCpuLoad,
        bool showGpuTemp,
        bool showGpuLoad,
        bool showRamUsage,
        bool showStorageTemp,
        bool showCpuPower)
    {
        _cpuTemp.Visible = showCpuTemp;
        _cpuLoad.Visible = showCpuLoad;
        _gpuTemp.Visible = showGpuTemp;
        _gpuLoad.Visible = showGpuLoad;
        _ramUsage.Visible = showRamUsage;
        _storageTemp.Visible = showStorageTemp;
        _cpuPower.Visible = showCpuPower;
    }

    public MonitoringChartSnapshot BuildSnapshot(int warnC, int critC, string statusHint)
    {
        critC = Math.Max(critC, warnC);
        var visible = AllChannels().Where(c => c.Visible && c.Points.Count > 0).ToList();
        var maxX = visible.SelectMany(c => c.Points).Select(p => p.X).DefaultIfEmpty(10).Max();

        var series = visible.Select(c => new MonitoringChartSeries
        {
            Title = c.Title,
            Color = c.Color,
            Scale = c.Scale,
            Points = c.Points.ToList()
        }).ToList();

        return new MonitoringChartSnapshot
        {
            Series = series,
            WarningTempC = warnC,
            CriticalTempC = critC,
            MaxTimeSeconds = Math.Max(maxX, 10),
            StatusHint = string.IsNullOrEmpty(statusHint) ? _title : $"{_title} • {statusHint}"
        };
    }

    public int TotalPointCount => AllChannels().Sum(c => c.Points.Count);

    private IEnumerable<SeriesChannel> AllChannels()
    {
        yield return _cpuTemp;
        yield return _cpuLoad;
        yield return _gpuTemp;
        yield return _gpuLoad;
        yield return _ramUsage;
        yield return _storageTemp;
        yield return _cpuPower;
    }

    private sealed class SeriesChannel
    {
        public SeriesChannel(string title, string color, string scale)
        {
            Title = title;
            Color = color;
            Scale = scale;
        }

        public string Title { get; }
        public string Color { get; }
        public string Scale { get; }
        public bool Visible { get; set; } = true;
        public List<(double X, double Y)> Points { get; } = new();

        public void Clear() => Points.Clear();

        public void Add(bool show, double? value, double x, int maxPoints)
        {
            if (!show || value is not { } v || double.IsNaN(v) || double.IsInfinity(v))
                return;
            Points.Add((x, v));
            while (Points.Count > maxPoints)
                Points.RemoveAt(0);
        }
    }
}
