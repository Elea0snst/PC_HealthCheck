using PC_HealthCheck.Core;

namespace PC_HealthCheck.Business;

/// <summary>Агрегаты сенсоров для графика и UI, когда вентиляторы/BIOS недоступны.</summary>
public static class MonitoringSensorAggregator
{
    public static bool HasFanSensors(DeviceSnapshot s) =>
        s.FanSensors.Any(f => f.Value > 0);

    public static bool HasLiveBiosSensors(DeviceSnapshot s) =>
        s.VrmChipsetSensors.Count > 0 ||
        s.AllHardwareSensors.Any(x =>
            string.Equals(x.HardwareGroup, "Motherboard", StringComparison.OrdinalIgnoreCase) &&
            x.Kind is SensorKind.Temperature or SensorKind.Voltage or SensorKind.Fan);

    public static bool HasCpuTemperature(DeviceSnapshot s) =>
        TryGetCpuTemperatureC(s) is not null;

    public static bool HasCpuLoad(DeviceSnapshot s) =>
        s.CpuLoadPercent is >= 0 and <= 100;

    public static double? TryGetCpuTemperatureC(DeviceSnapshot s)
    {
        if (s.CpuTemperatureC is { } direct and > -20 and < 150)
            return direct;

        var fromCpuSensors = s.CpuSensors
            .Where(x => x.Kind == SensorKind.Temperature)
            .Where(x => x.Value > 0 && x.Value < 150)
            .Select(x => x.Value)
            .ToArray();
        if (fromCpuSensors.Length > 0)
            return fromCpuSensors.Max();

        var fromAll = s.AllHardwareSensors
            .Where(x => string.Equals(x.HardwareGroup, "Cpu", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Kind == SensorKind.Temperature)
            .Where(x => x.Value > 0 && x.Value < 150)
            .Where(x =>
                x.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("temp", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToArray();
        if (fromAll.Length > 0)
            return fromAll.Max();

        return null;
    }

    public static double? TryGetGpuTemperatureC(DeviceSnapshot s)
    {
        var temps = s.AllHardwareSensors
            .Where(x => string.Equals(x.HardwareGroup, "Gpu", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Kind == SensorKind.Temperature)
            .Where(x => x.Value > 0 && x.Value < 150)
            .Select(x => x.Value)
            .ToArray();
        return temps.Length == 0 ? null : temps.Max();
    }

    public static double? TryGetGpuLoadPercent(DeviceSnapshot s)
    {
        var loads = s.AllHardwareSensors
            .Where(x => string.Equals(x.HardwareGroup, "Gpu", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Kind == SensorKind.Load)
            .Where(x => x.Value >= 0 && x.Value <= 100)
            .Where(x =>
                x.Name.Contains("core", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("gpu", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("d3d", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("3d", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("load", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToArray();
        return loads.Length == 0 ? null : loads.Max();
    }

    public static double? TryGetMaxFanRpm(DeviceSnapshot s)
    {
        if (s.FanSensors.Count == 0) return null;
        var rpm = s.FanSensors.Where(f => f.Value > 0).Select(f => f.Value).ToArray();
        return rpm.Length == 0 ? null : rpm.Max();
    }

    public static double? TryGetMaxStorageTempC(DeviceSnapshot s)
    {
        if (s.StorageTemperatureSensors.Count == 0) return null;
        var t = s.StorageTemperatureSensors
            .Where(x => x.Value > 0 && x.Value < 120)
            .Select(x => x.Value)
            .ToArray();
        return t.Length == 0 ? null : t.Max();
    }

    public static double? TryGetCpuPowerW(DeviceSnapshot s)
    {
        var p = s.AllHardwareSensors
            .Where(x => string.Equals(x.HardwareGroup, "Cpu", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Kind == SensorKind.Power)
            .Where(x => x.Value > 0 && x.Value < 500)
            .Select(x => x.Value)
            .ToArray();
        return p.Length == 0 ? null : p.Max();
    }

    public static MonitoringChartCapabilities Analyze(DeviceSnapshot s)
    {
        var hasCpuTemp = HasCpuTemperature(s);
        var hasFans = HasFanSensors(s);
        var hasBios = HasLiveBiosSensors(s);
        var limited = !hasCpuTemp || !hasFans || !hasBios;

        return new MonitoringChartCapabilities(
            hasFans,
            hasBios,
            hasCpuTemp,
            HasCpuLoad(s),
            TryGetGpuTemperatureC(s) is not null,
            TryGetGpuLoadPercent(s) is not null,
            TryGetMaxStorageTempC(s) is not null,
            TryGetCpuPowerW(s) is not null,
            limited);
    }
}

public sealed record MonitoringChartCapabilities(
    bool HasFans,
    bool HasBiosSensors,
    bool HasCpuTemp,
    bool HasCpuLoad,
    bool HasGpuTemp,
    bool HasGpuLoad,
    bool HasStorageTemp,
    bool HasCpuPower,
    bool IsLimitedMonitoring);
