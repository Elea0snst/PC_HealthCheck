using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using PC_HealthCheck.Core;

namespace PC_HealthCheck.DAL;

/// <summary>WMI / Performance Counter — обход, когда LHM на ноутбуке не отдаёт CPU/RAM.</summary>
internal static class WindowsMonitoringFallback
{
    private static PerformanceCounter? _cpuCounter;
    private static bool _cpuCounterPrimed;
    private static double? _lastPerfCpuLoad;

    public static void Apply(DeviceSnapshot s)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        EnsureRamFromOs(s);

        if (s.CpuLoadPercent is null)
            s.CpuLoadPercent = TryReadCpuLoadWmi() ?? _lastPerfCpuLoad ?? TryReadCpuLoadPerformanceCounter();

        if (s.CpuTemperatureC is null)
            s.CpuTemperatureC = TryReadThermalZoneCelsius() ?? TryReadCpuTempFromSensors(s);

        if (s.CpuLoadPercent is { } load)
            AddSyntheticSensor(s, "Cpu", SensorKind.Load, "WMI/PerfCounter CPU Total", load, "%");

        if (s.CpuTemperatureC is { } t)
            AddSyntheticSensor(s, "Cpu", SensorKind.Temperature, "WMI/thermal CPU", t, "°C");
    }

    private static void EnsureRamFromOs(DeviceSnapshot s)
    {
        if (s.TotalRamBytes > 0 && s.FreeRamBytes >= 0)
            return;

        try
        {
            var mem = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (mem > 0 && s.TotalRamBytes <= 0)
                s.TotalRamBytes = (long)mem;
        }
        catch
        {
            // ignore
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var totalKb = Convert.ToInt64(obj["TotalVisibleMemorySize"] ?? 0L);
                var freeKb = Convert.ToInt64(obj["FreePhysicalMemory"] ?? 0L);
                if (totalKb > 0)
                    s.TotalRamBytes = totalKb * 1024;
                if (freeKb >= 0)
                    s.FreeRamBytes = freeKb * 1024;
                break;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static double? TryReadCpuLoadWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LoadPercentage FROM Win32_Processor");
            var values = new List<double>();
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["LoadPercentage"] is null)
                    continue;
                var v = Convert.ToDouble(obj["LoadPercentage"]);
                if (v >= 0 && v <= 100)
                    values.Add(v);
            }

            return values.Count == 0 ? null : values.Average();
        }
        catch
        {
            return null;
        }
    }

    private static double? TryReadCpuLoadPerformanceCounter()
    {
        try
        {
            _cpuCounter ??= new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            if (!_cpuCounterPrimed)
            {
                _ = _cpuCounter.NextValue();
                _cpuCounterPrimed = true;
                return null;
            }

            var v = _cpuCounter.NextValue();
            if (v >= 0 && v <= 100)
            {
                _lastPerfCpuLoad = v;
                return v;
            }
        }
        catch
        {
            _cpuCounter = null;
            _cpuCounterPrimed = false;
        }

        return _lastPerfCpuLoad;
    }

    private static double? TryReadThermalZoneCelsius()
    {
        try
        {
            double? best = null;
            using var searcher = new ManagementObjectSearcher(@"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject obj in searcher.Get())
            {
                var raw = Convert.ToDouble(obj["CurrentTemperature"] ?? 0d);
                var c = raw / 10.0 - 273.15;
                if (c is > -20 and < 150)
                    best = best is null ? c : Math.Max(best.Value, c);
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    private static double? TryReadCpuTempFromSensors(DeviceSnapshot s)
    {
        var temps = s.AllHardwareSensors
            .Where(x => x.Kind == SensorKind.Temperature)
            .Where(x => x.Value > 0 && x.Value < 150)
            .Where(x =>
                string.Equals(x.HardwareGroup, "Cpu", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToArray();
        return temps.Length == 0 ? null : temps.Max();
    }

    private static void AddSyntheticSensor(
        DeviceSnapshot s,
        string group,
        SensorKind kind,
        string name,
        double value,
        string unit)
    {
        if (s.AllHardwareSensors.Any(x => x.Name == name))
            return;

        var reading = new SensorReading
        {
            Name = name,
            HardwareGroup = group,
            Kind = kind,
            Unit = unit,
            Value = value,
            TimestampLocal = DateTime.Now
        };
        s.AllHardwareSensors.Add(reading);
        if (string.Equals(group, "Cpu", StringComparison.OrdinalIgnoreCase))
            s.CpuSensors.Add(reading);
    }
}
