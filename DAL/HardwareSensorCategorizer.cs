using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using PC_HealthCheck.Core;

namespace PC_HealthCheck.DAL;

/// <summary>Разносит показания LHM по группам для UI и отчётов (ядра, VRM/PCH, вентиляторы, накопители).</summary>
internal static class HardwareSensorCategorizer
{
    private static readonly Regex CoreClockRx = new(
        @"Core\s*#\d+|CPU\s*Core\s*#\d+|Core\s+\d+|CCD\s*\d+\s*Core|Core\s+Max|Effective\s*Clock|IA\s*Cores",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CoreVoltageRx = new(
        @"Core\s*#\d+|VID|Vcore|CPU\s*Core|CCD|Core\s+Voltage|IA\s*Cores|SoC\s*Voltage|SVI",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Apply(DeviceSnapshot s)
    {
        s.CpuPerCoreClocks.Clear();
        s.CpuPerCoreVoltages.Clear();
        s.VrmChipsetSensors.Clear();
        s.FanSensors.Clear();
        s.StorageTemperatureSensors.Clear();

        foreach (var r in s.AllHardwareSensors)
        {
            if (r.Kind == SensorKind.Fan)
                s.FanSensors.Add(Copy(r));

            if (r.Kind == SensorKind.Temperature && IsStorageTemperature(r))
                s.StorageTemperatureSensors.Add(Copy(r));

            if (string.Equals(r.HardwareGroup, HardwareType.Cpu.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (r.Kind == SensorKind.Clock && CoreClockRx.IsMatch(r.Name))
                    s.CpuPerCoreClocks.Add(Copy(r));
                else if (r.Kind == SensorKind.Voltage && CoreVoltageRx.IsMatch(r.Name))
                    s.CpuPerCoreVoltages.Add(Copy(r));
            }

            if (IsVrmOrChipset(r))
                s.VrmChipsetSensors.Add(Copy(r));
        }

        // Fallback: на некоторых платах имена не содержат Core#/VID, но данные есть как CPU Clock/Voltage.
        if (s.CpuPerCoreClocks.Count == 0)
        {
            foreach (var r in s.AllHardwareSensors)
            {
                if (!string.Equals(r.HardwareGroup, HardwareType.Cpu.ToString(), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (r.Kind != SensorKind.Clock)
                    continue;
                if (r.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
                    continue;
                s.CpuPerCoreClocks.Add(Copy(r));
            }
        }

        if (s.CpuPerCoreVoltages.Count == 0)
        {
            foreach (var r in s.AllHardwareSensors)
            {
                if (!string.Equals(r.HardwareGroup, HardwareType.Cpu.ToString(), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (r.Kind != SensorKind.Voltage)
                    continue;
                s.CpuPerCoreVoltages.Add(Copy(r));
            }
        }
    }

    private static bool IsStorageTemperature(SensorReading r)
    {
        if (string.Equals(r.HardwareGroup, HardwareType.Storage.ToString(), StringComparison.OrdinalIgnoreCase))
            return true;

        var n = r.Name;
        return n.Contains("SSD", StringComparison.OrdinalIgnoreCase)
               || n.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
               || n.Contains("HDD", StringComparison.OrdinalIgnoreCase)
               || n.Contains("Drive", StringComparison.OrdinalIgnoreCase)
               || n.Contains("Disk", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVrmOrChipset(SensorReading r)
    {
        if (r.Kind == SensorKind.Fan)
            return false;

        if (string.Equals(r.HardwareGroup, HardwareType.Motherboard.ToString(), StringComparison.OrdinalIgnoreCase))
            return r.Kind is SensorKind.Voltage or SensorKind.Temperature or SensorKind.Power or SensorKind.Clock;

        var n = r.Name;
        if (n.Contains("VRM", StringComparison.OrdinalIgnoreCase))
            return true;
        if (n.Contains("PCH", StringComparison.OrdinalIgnoreCase) || n.Contains("Chipset", StringComparison.OrdinalIgnoreCase))
            return true;
        return n.Contains("SoC", StringComparison.OrdinalIgnoreCase) && r.Kind == SensorKind.Voltage;
    }

    private static SensorReading Copy(SensorReading r) => new()
    {
        Name = r.Name,
        HardwareGroup = r.HardwareGroup,
        Kind = r.Kind,
        Unit = r.Unit,
        Value = r.Value,
        TimestampLocal = r.TimestampLocal
    };
}
