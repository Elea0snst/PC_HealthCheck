namespace PC_HealthCheck.Core;

/// <summary>Пользовательские настройки (ТЗ 2.5.2).</summary>
public sealed class UserAppSettings
{
    public int MonitoringIntervalSeconds { get; set; } = 1;
    public int CpuTempWarningCelsius { get; set; } = 75;
    public int CpuTempCriticalCelsius { get; set; } = 90;
    public bool UseDarkTheme { get; set; }
    /// <summary>Комментарий к отчёту (ТЗ 2.4.1).</summary>
    public string ReportComment { get; set; } = "";
}
