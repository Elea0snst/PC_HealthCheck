namespace PC_HealthCheck.Visualization;

public sealed class MonitoringChartSnapshot
{
    public required IReadOnlyList<MonitoringChartSeries> Series { get; init; }
    public int WarningTempC { get; init; }
    public int CriticalTempC { get; init; }
    public double MaxTimeSeconds { get; init; }
    public string StatusHint { get; init; } = "";
}

public sealed class MonitoringChartSeries
{
    public required string Title { get; init; }
    public required string Color { get; init; }
    /// <summary>Temp — левая шкала °C; Percent — правая шкала 0–100.</summary>
    public required string Scale { get; init; }
    public required IReadOnlyList<(double X, double Y)> Points { get; init; }
}
