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
        else if (snapshot.RamUsagePercent >= 80)
            lines.Add($"RAM близка к насыщению ({snapshot.RamUsagePercent:F0}%). Рекомендуется закрыть фоновые приложения.");

        if (snapshot.LogicalDisks.Count > 0)
        {
            foreach (var l in snapshot.LogicalDisks)
            {
                if (l.SizeBytes <= 0) continue;
                var used = (1.0 - (double)l.FreeBytes / l.SizeBytes) * 100.0;
                if (used >= 95)
                {
                    lines.Add($"Критично: диск {l.DeviceId} заполнен на {used:F0}%.");
                    hasAlert = true;
                }
                else if (used >= 85)
                    lines.Add($"Предупреждение: диск {l.DeviceId} заполнен на {used:F0}%.");
            }
        }

        foreach (var d in snapshot.PhysicalDisks)
        {
            if (d.SmartPredictFailure != true) continue;
            lines.Add($"Диск «{d.Model}»: предсказание отказа по SMART (WMI).");
            hasAlert = true;
        }

        // Узкие места и рекомендации (ТЗ 2.3.2)
        if (snapshot.CpuLoadPercent is { } cpuLoad && snapshot.RamUsagePercent < 75 && cpuLoad >= 90)
            lines.Add("Вероятное узкое место: CPU. Рекомендация: проверить охлаждение и фоновые задачи, рассмотреть апгрейд CPU.");

        if (snapshot.RamUsagePercent >= 90 && (snapshot.CpuLoadPercent ?? 0) < 70)
            lines.Add("Вероятное узкое место: RAM. Рекомендация: увеличить объём памяти и сократить количество резидентных процессов.");

        if (snapshot.LogicalDisks.Any(d => d.SizeBytes > 0 && (1.0 - (double)d.FreeBytes / d.SizeBytes) >= 0.9))
            lines.Add("Рекомендация: освободить место на системном диске; при HDD рассмотреть переход на SSD/NVMe.");

        // Bottleneck (расширение): CPU vs GPU vs RAM (эвристика по сенсорам LHM)
        var gpuLoad = TryGetGpuCoreLoadPercent(snapshot);
        if (snapshot.CpuLoadPercent is { } c && gpuLoad is { } g)
        {
            if (g >= 90 && c < 70)
                lines.Add("Bottleneck: GPU загружена сильно, CPU относительно свободен — упор в видеокарту (ограничение по GPU).");
            else if (c >= 90 && g < 60)
                lines.Add("Bottleneck: CPU загружен сильно, GPU недогружена — упор в процессор/потоки (CPU bottleneck).");
        }

        if (snapshot.RamUsagePercent >= 95 && (snapshot.CpuLoadPercent ?? 0) >= 60)
            lines.Add("Bottleneck: высокая загрузка RAM может вызывать подкачку и фризы. Рекомендация: закрыть фоновые приложения/увеличить RAM.");

        if (!hasAlert)
            lines.Add("По выбранным правилам критических отклонений не обнаружено.");

        return lines;
    }

    private static double? TryGetGpuCoreLoadPercent(DeviceSnapshot snapshot)
    {
        // Ищем в AllHardwareSensors: группа Gpu, Kind=Load, имя похоже на core/load.
        var loads = snapshot.AllHardwareSensors
            .Where(x => string.Equals(x.HardwareGroup, "Gpu", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Kind == SensorKind.Load)
            .Where(x => x.Value >= 0 && x.Value <= 100)
            .Where(x =>
                x.Name.Contains("core", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("gpu", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("load", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToArray();

        if (loads.Length == 0) return null;
        return loads.Max();
    }
}
