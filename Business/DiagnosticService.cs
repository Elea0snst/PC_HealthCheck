using PC_HealthCheck.Core;

namespace PC_HealthCheck.Business;

/// <summary>Автоматическая диагностика по снимку (ТЗ 2.3.1 — базовая реализация).</summary>
public sealed class DiagnosticService
{
    public IReadOnlyList<string> Analyze(DeviceSnapshot snapshot, UserAppSettings settings)
    {
        var lines = new List<string>();
        var warn = Math.Clamp(settings.CpuTempWarningCelsius, 30, 120);
        var crit = Math.Max(Math.Clamp(settings.CpuTempCriticalCelsius, 40, 125), warn);
        var hasAlert = false;

        if (snapshot.CpuTemperatureC is { } cpuT)
        {
            if (cpuT >= crit)
            {
                lines.Add($"Критично: температура CPU {cpuT:F1} °C (порог {crit} °C).");
                hasAlert = true;
            }
            else if (cpuT >= warn)
            {
                lines.Add($"Предупреждение: температура CPU {cpuT:F1} °C (порог {warn} °C).");
                hasAlert = true;
            }
        }
        else
            lines.Add("Температура CPU недоступна (ограниченный режим датчиков).");

        if (snapshot.CpuLoadPercent is >= 98 && snapshot.CpuTemperatureC is { } t && t >= warn)
        {
            lines.Add("Одновременно высокие загрузка CPU и температура — возможен троттлинг.");
            hasAlert = true;
        }

        if (snapshot.RamUsagePercent >= 95)
        {
            lines.Add($"Высокая загрузка RAM: {snapshot.RamUsagePercent:F0}%.");
            hasAlert = true;
        }

        foreach (var d in snapshot.PhysicalDisks)
        {
            if (d.SmartPredictFailure != true) continue;
            lines.Add($"Диск «{d.Model}»: предсказание отказа по SMART (WMI).");
            hasAlert = true;
        }

        if (!hasAlert)
            lines.Add("По выбранным правилам критических отклонений не обнаружено.");

        return lines;
    }
}
