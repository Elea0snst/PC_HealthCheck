using System.Runtime.InteropServices;
using PC_HealthCheck.Core;
using PC_HealthCheck.DAL;

namespace PC_HealthCheck.Business;

/// <summary>Дополняет снимок для UI/графика/отчётов (ноутбуки, Linux/macOS).</summary>
public static class SnapshotEnricher
{
    public static DeviceSnapshot Enrich(DeviceSnapshot s)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            WindowsMonitoringFallback.Apply(s);

        if (s.CpuTemperatureC is null)
            s.CpuTemperatureC = MonitoringSensorAggregator.TryGetCpuTemperatureC(s);

        if (s.CpuLoadPercent is null)
            s.CpuLoadPercent = TryGetCpuLoadPercent(s);

        return s;
    }

    private static double? TryGetCpuLoadPercent(DeviceSnapshot s)
    {
        var loads = s.AllHardwareSensors
            .Where(x => string.Equals(x.HardwareGroup, "Cpu", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Kind == SensorKind.Load)
            .Where(x => x.Value >= 0 && x.Value <= 100)
            .Select(x => x.Value)
            .ToArray();
        return loads.Length == 0 ? null : loads.Max();
    }
}
