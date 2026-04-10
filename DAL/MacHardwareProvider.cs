using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using PC_HealthCheck.Core;

namespace PC_HealthCheck.DAL;

public sealed class MacHardwareProvider : IHardwareProvider
{
    private DeviceSnapshot _snapshot = new();
    private long _prevTicks;
    private long _prevIdle;
    private DateTime _prevReadUtc = DateTime.MinValue;

    public bool IsInitialized { get; private set; }

    public Task<bool> InitializeAsync()
    {
        _snapshot = new DeviceSnapshot
        {
            CpuName = Sysctl("-n machdep.cpu.brand_string") ?? "Apple CPU",
            CpuManufacturer = "Apple",
            BiosVersion = "macOS",
            RamType = "Unified/Unknown"
        };
        IsInitialized = true;
        return Task.FromResult(true);
    }

    public Task<DeviceSnapshot> ReadSnapshotAsync()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Provider is not initialized.");

        _snapshot.TimestampUtc = DateTime.UtcNow;
        _snapshot.TotalRamBytes = ParseLong(Sysctl("-n hw.memsize"));
        _snapshot.FreeRamBytes = EstimateFreeMemoryBytes();
        _snapshot.CpuLoadPercent = ReadCpuLoad();
        _snapshot.CpuTemperatureC = ReadPowermetricsCpuTemp();

        _snapshot.AllHardwareSensors.Clear();
        if (_snapshot.CpuTemperatureC is { } t)
        {
            _snapshot.AllHardwareSensors.Add(new SensorReading
            {
                Name = "powermetrics / CPU die temperature",
                HardwareGroup = "Cpu",
                Kind = SensorKind.Temperature,
                Unit = "°C",
                Value = t,
                TimestampLocal = DateTime.Now
            });
        }

        _snapshot.CpuSensors = _snapshot.AllHardwareSensors
            .Where(x => x.HardwareGroup.Equals("Cpu", StringComparison.OrdinalIgnoreCase))
            .ToList();
        HardwareSensorCategorizer.Apply(_snapshot);

        _snapshot.LogicalDisks = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new LogicalDiskInfo
            {
                DeviceId = d.Name,
                VolumeName = d.VolumeLabel,
                FileSystem = d.DriveFormat,
                SizeBytes = d.TotalSize,
                FreeBytes = d.AvailableFreeSpace
            }).ToList();

        _snapshot.NetworkAdapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => new NetworkAdapterInfo
            {
                Name = n.Name,
                Manufacturer = "Apple",
                MacAddress = n.GetPhysicalAddress().ToString(),
                NetEnabled = true,
                NetConnectionId = n.Description,
                SpeedBitsPerSec = n.Speed > 0 ? (ulong)n.Speed : 0,
                AdapterType = n.NetworkInterfaceType.ToString()
            }).ToList();

        return Task.FromResult(CloneSnapshot(_snapshot));
    }

    private double? ReadCpuLoad()
    {
        var text = TryRun("sh", "-lc \"top -l 1 -n 0 | grep 'CPU usage'\""); // macOS top format
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Example: CPU usage: 7.21% user, 5.01% sys, 87.76% idle
        var idleMatch = System.Text.RegularExpressions.Regex.Match(text, @"([\d\.]+)%\s*idle", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!idleMatch.Success)
            return null;
        if (!double.TryParse(idleMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var idle))
            return null;
        return Math.Clamp(100.0 - idle, 0, 100);
    }

    private double? ReadPowermetricsCpuTemp()
    {
        // Requires elevated privileges on most macOS builds; best effort.
        var txt = TryRun("sh", "-lc \"powermetrics -n 1 -s smc 2>/dev/null | grep -i 'CPU die temperature'\"");
        if (string.IsNullOrWhiteSpace(txt))
            return null;
        var m = System.Text.RegularExpressions.Regex.Match(txt, @"([-+]?\d+(\.\d+)?)");
        if (!m.Success)
            return null;
        if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return null;
        return v is > -20 and < 150 ? v : null;
    }

    private long EstimateFreeMemoryBytes()
    {
        var pageSize = ParseLong(Sysctl("-n hw.pagesize"));
        if (pageSize <= 0) pageSize = 4096;
        var vm = TryRun("vm_stat", "");
        if (string.IsNullOrWhiteSpace(vm))
            return 0;
        long free = ParseVmStat(vm, "Pages free");
        long inactive = ParseVmStat(vm, "Pages inactive");
        long speculative = ParseVmStat(vm, "Pages speculative");
        return (free + inactive + speculative) * pageSize;
    }

    private static long ParseVmStat(string text, string key)
    {
        var line = text.Split('\n').FirstOrDefault(x => x.TrimStart().StartsWith(key, StringComparison.OrdinalIgnoreCase));
        if (line is null) return 0;
        var cleaned = new string(line.Where(char.IsDigit).ToArray());
        return long.TryParse(cleaned, out var v) ? v : 0;
    }

    private static string? Sysctl(string args) => TryRun("sysctl", args)?.Trim();

    private static string? TryRun(string fileName, string args)
    {
        try
        {
            using var p = new Process();
            p.StartInfo.FileName = fileName;
            p.StartInfo.Arguments = args;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            if (p.ExitCode != 0) return null;
            return output;
        }
        catch
        {
            return null;
        }
    }

    private static long ParseLong(string? text) => long.TryParse(text, out var v) ? v : 0;

    private static DeviceSnapshot CloneSnapshot(DeviceSnapshot s) => new()
    {
        TimestampUtc = s.TimestampUtc,
        CpuName = s.CpuName,
        CpuManufacturer = s.CpuManufacturer,
        CpuTemperatureC = s.CpuTemperatureC,
        CpuLoadPercent = s.CpuLoadPercent,
        TotalRamBytes = s.TotalRamBytes,
        FreeRamBytes = s.FreeRamBytes,
        RamType = s.RamType,
        LogicalDisks = s.LogicalDisks.ToList(),
        NetworkAdapters = s.NetworkAdapters.ToList(),
        CpuSensors = s.CpuSensors.ToList(),
        AllHardwareSensors = s.AllHardwareSensors.ToList(),
        CpuPerCoreClocks = s.CpuPerCoreClocks.ToList(),
        CpuPerCoreVoltages = s.CpuPerCoreVoltages.ToList(),
        VrmChipsetSensors = s.VrmChipsetSensors.ToList(),
        FanSensors = s.FanSensors.ToList()
    };

    public void Dispose()
    {
    }
}
