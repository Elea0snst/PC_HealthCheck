using System.Runtime.InteropServices;
using PC_HealthCheck.Core;

namespace PC_HealthCheck.Business;

/// <summary>Дополнительные строки для отчётов (мониторинг, ОС, возможности датчиков).</summary>
internal static class ReportSnapshotExtras
{
    public static IReadOnlyList<string> EnvironmentLines() =>
    [
        $"ОС: {RuntimeInformation.OSDescription}",
        $"Архитектура: {RuntimeInformation.OSArchitecture} / {RuntimeInformation.ProcessArchitecture}",
        $".NET: {RuntimeInformation.FrameworkDescription}",
        $"Машина: {Environment.MachineName}, пользователь: {Environment.UserName}"
    ];

    public static IReadOnlyList<string> MonitoringSummaryLines(DeviceSnapshot s)
    {
        var caps = MonitoringSensorAggregator.Analyze(s);
        var cpuT = MonitoringSensorAggregator.TryGetCpuTemperatureC(s);
        var gpuT = MonitoringSensorAggregator.TryGetGpuTemperatureC(s);
        var gpuL = MonitoringSensorAggregator.TryGetGpuLoadPercent(s);

        return
        [
            $"CPU температура (агрегат): {(cpuT?.ToString("F1") ?? "N/A")} °C",
            $"CPU загрузка: {(s.CpuLoadPercent?.ToString("F0") ?? "N/A")} %",
            $"GPU температура: {(gpuT?.ToString("F1") ?? "N/A")} °C",
            $"GPU загрузка: {(gpuL?.ToString("F0") ?? "N/A")} %",
            $"Вентиляторы (RPM): {(caps.HasFans ? "да" : "нет")}",
            $"Live BIOS/VRM: {(caps.HasBiosSensors ? "да" : "нет")}",
            $"Темп. накопителей: {(caps.HasStorageTemp ? "да" : "нет")}",
            $"Мощность CPU: {(caps.HasCpuPower ? "да" : "нет")}",
            $"Сенсоров всего: {s.AllHardwareSensors.Count}, CPU: {s.CpuSensors.Count}, вентиляторов: {s.FanSensors.Count}",
            caps.IsLimitedMonitoring
                ? "Режим ограниченного мониторинга (типично ноутбук/OEM): график использует CPU/RAM/GPU-прокси."
                : "Режим полного мониторинга: доступны расширенные сенсоры."
        ];
    }
}
