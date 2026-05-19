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
        _snapshot.CpuTemperatureC = ReadCpuTemperatureBestEffort();
        PopulateCpuTopologyFromSysctl();
        PopulateGpusFromSystemProfiler();

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

        return Task.FromResult(DeviceSnapshotCloner.Clone(_snapshot));
    }

    private void PopulateCpuTopologyFromSysctl()
    {
        _snapshot.CpuCores = (int)ParseLong(Sysctl("-n hw.physicalcpu"));
        _snapshot.CpuThreads = (int)ParseLong(Sysctl("-n hw.logicalcpu"));
        if (_snapshot.CpuCores <= 0) _snapshot.CpuCores = Environment.ProcessorCount;
        if (_snapshot.CpuThreads <= 0) _snapshot.CpuThreads = Environment.ProcessorCount;
        var hz = ParseLong(Sysctl("-n hw.cpufrequency_max"));
        if (hz > 0)
            _snapshot.CpuMaxClockMHz = (uint)(hz / 1_000_000);
    }

    private void PopulateGpusFromSystemProfiler()
    {
        if (_snapshot.Gpus.Count > 0)
            return;

        var txt = TryRun("sh", "-lc \"system_profiler SPDisplaysDataType 2>/dev/null\"");
        if (string.IsNullOrWhiteSpace(txt))
            return;

        string? current = null;
        foreach (var raw in txt.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Contains("Chipset Model:", StringComparison.OrdinalIgnoreCase))
            {
                current = line[(line.IndexOf(':') + 1)..].Trim();
                if (!string.IsNullOrEmpty(current))
                {
                    _snapshot.Gpus.Add(new GpuInfo
                    {
                        Name = current,
                        Manufacturer = "Apple",
                        DriverVersion = "macOS"
                    });
                }
            }
        }
    }

    private double? ReadCpuTemperatureBestEffort()
    {
        var t = ReadPowermetricsCpuTemp();
        if (t is not null)
            return t;

        var sensors = TryRun("sh", "-lc \"sensors 2>/dev/null | grep -iE 'Package id|CPU|Tctl|Tdie'\"");
        if (string.IsNullOrWhiteSpace(sensors))
            return null;

        double? best = null;
        foreach (var line in sensors.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"([-+]?\d+(\.\d+)?)");
            if (!m.Success)
                continue;
            if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                continue;
            if (v is > -20 and < 150)
                best = best is null ? v : Math.Max(best.Value, v);
        }

        return best;
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

    public void Dispose()
    {
    }
}
