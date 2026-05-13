namespace PC_HealthCheck.Core;

/// <summary>Пользовательские настройки (ТЗ 2.5.2).</summary>
public sealed class UserAppSettings
{
    public int MonitoringIntervalSeconds { get; set; } = 1;
    public int CpuTempWarningCelsius { get; set; } = 75;
    public int CpuTempCriticalCelsius { get; set; } = 90;
    /// <summary>Требовать явное подтверждение перед запуском стресс-тестов (ТЗ 6.2).</summary>
    public bool StressTestConfirmationAccepted { get; set; }
    public bool UseDarkTheme { get; set; }
    /// <summary>Комментарий к отчёту (ТЗ 2.4.1).</summary>
    public string ReportComment { get; set; } = "";

    /// <summary>URL локального Ollama (по умолчанию localhost).</summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Модель Ollama для AI-диагностики.</summary>
    public string OllamaModel { get; set; } = "llama3.2";
}
