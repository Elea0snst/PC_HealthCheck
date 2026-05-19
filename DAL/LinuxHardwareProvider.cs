using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using PC_HealthCheck.Core;

namespace PC_HealthCheck.DAL;

public sealed class LinuxHardwareProvider : IHardwareProvider
{
    private DeviceSnapshot _snapshot = new();
    private long _prevTotalTicks;
    private long _prevIdleTicks;
    private DateTime _prevCpuReadUtc = DateTime.MinValue;

    public bool IsInitialized { get; private set; }

    public Task<bool> InitializeAsync()
    {
        _snapshot = new DeviceSnapshot
        {
            CpuName = ReadCpuModelName(),
            CpuManufacturer = "Linux",
            CpuInstructionSets = "cpuid unavailable",
            MotherboardManufacturer = "Unknown",
            MotherboardModel = "Unknown",
            BiosVersion = "Linux",
            RamType = "Unknown"
        };
        IsInitialized = true;
        return Task.FromResult(true);
    }

    public Task<DeviceSnapshot> ReadSnapshotAsync()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Provider is not initialized.");

        _snapshot.TimestampUtc = DateTime.UtcNow;
        ReadMemoryFromProc();
        _snapshot.CpuLoadPercent = ReadCpuLoadFromProcStat();
        _snapshot.CpuTemperatureC = ReadCpuTemperature();
        _snapshot.CpuSensors.Clear();
        _snapshot.AllHardwareSensors.Clear();
        _snapshot.CpuPerCoreClocks.Clear();
        _snapshot.CpuPerCoreVoltages.Clear();
        _snapshot.VrmChipsetSensors.Clear();
        _snapshot.FanSensors.Clear();
        _snapshot.StorageTemperatureSensors.Clear();

        PopulateFromLmSensors();
        PopulateFromHwmon();
        PopulateCpuTopologyFromProc();
        PopulateGpusBestEffort();
        TryPopulateNvidiaSensors();
        HardwareSensorCategorizer.Apply(_snapshot);

        if (_snapshot.CpuSensors.Count == 0)
            _snapshot.CpuSensors = _snapshot.AllHardwareSensors
                .Where(x => string.Equals(x.HardwareGroup, "Cpu", StringComparison.OrdinalIgnoreCase))
                .ToList();

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
                Manufacturer = "Linux",
                MacAddress = n.GetPhysicalAddress().ToString(),
                NetEnabled = true,
                NetConnectionId = n.Description,
                SpeedBitsPerSec = n.Speed > 0 ? (ulong)n.Speed : 0,
                AdapterType = n.NetworkInterfaceType.ToString()
            }).ToList();

        return Task.FromResult(DeviceSnapshotCloner.Clone(_snapshot));
    }

    private void PopulateCpuTopologyFromProc()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/cpuinfo");
            _snapshot.CpuCores = lines.Count(l => l.StartsWith("processor", StringComparison.OrdinalIgnoreCase));
            if (_snapshot.CpuCores <= 0) _snapshot.CpuCores = Environment.ProcessorCount;
            _snapshot.CpuThreads = _snapshot.CpuCores;
        }
        catch
        {
            _snapshot.CpuCores = Environment.ProcessorCount;
            _snapshot.CpuThreads = Environment.ProcessorCount;
        }
    }

    private void PopulateFromHwmon()
    {
        try
        {
            foreach (var hwmonDir in Directory.EnumerateDirectories("/sys/class/hwmon"))
            {
                var namePath = Path.Combine(hwmonDir, "name");
                if (!File.Exists(namePath))
                    continue;
                var chip = File.ReadAllText(namePath).Trim();
                var group = InferGroup(chip);

                foreach (var input in Directory.EnumerateFiles(hwmonDir, "*_input"))
                {
                    var labelFile = input.Replace("_input", "_label", StringComparison.Ordinal);
                    var key = File.Exists(labelFile) ? File.ReadAllText(labelFile).Trim() : Path.GetFileName(input);
                    if (!double.TryParse(File.ReadAllText(input).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
                        continue;

                    var (kind, unit, value) = NormalizeHwmonReading(key, raw);
                    _snapshot.AllHardwareSensors.Add(new SensorReading
                    {
                        Name = $"{chip} / {key}",
                        HardwareGroup = group,
                        Kind = kind,
                        Unit = unit,
                        Value = value,
                        TimestampLocal = DateTime.Now
                    });
                }
            }
        }
        catch
        {
            // best effort
        }
    }

    private static (SensorKind Kind, string Unit, double Value) NormalizeHwmonReading(string key, double raw)
    {
        var k = key.ToLowerInvariant();
        if (k.Contains("temp"))
        {
            var c = raw > 500 ? raw / 1000.0 : raw;
            return (SensorKind.Temperature, "°C", c);
        }
        if (k.Contains("fan"))
            return (SensorKind.Fan, "RPM", raw);
        if (k.Contains("in") || k.Contains("volt"))
            return (SensorKind.Voltage, "V", raw > 20 ? raw / 1000.0 : raw);
        if (k.Contains("power"))
            return (SensorKind.Power, "W", raw > 500 ? raw / 1_000_000.0 : raw);
        return (SensorKind.Other, "", raw);
    }

    private void PopulateGpusBestEffort()
    {
        if (_snapshot.Gpus.Count > 0)
            return;

        var lspci = TryRun("sh", "-lc \"lspci 2>/dev/null | grep -iE 'vga|3d|display'\"");
        if (string.IsNullOrWhiteSpace(lspci))
            return;

        foreach (var line in lspci.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = line;
            var colon = line.IndexOf(':');
            if (colon >= 0 && colon + 1 < line.Length)
                name = line[(colon + 1)..].Trim();
            _snapshot.Gpus.Add(new GpuInfo
            {
                Name = name,
                Manufacturer = "Linux",
                DriverVersion = "lspci"
            });
        }
    }

    private void TryPopulateNvidiaSensors()
    {
        var outText = TryRun("nvidia-smi", "--query-gpu=name,temperature.gpu,utilization.gpu,memory.total --format=csv,noheader,nounits");
        if (string.IsNullOrWhiteSpace(outText))
            return;

        foreach (var line in outText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                continue;

            var gpuName = parts[0];
            if (_snapshot.Gpus.All(g => !g.Name.Contains(gpuName, StringComparison.OrdinalIgnoreCase)))
            {
                _snapshot.Gpus.Add(new GpuInfo
                {
                    Name = gpuName,
                    Manufacturer = "NVIDIA",
                    DriverVersion = "nvidia-smi"
                });
            }

            if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
            {
                _snapshot.AllHardwareSensors.Add(new SensorReading
                {
                    Name = $"{gpuName} / GPU Temperature",
                    HardwareGroup = "Gpu",
                    Kind = SensorKind.Temperature,
                    Unit = "°C",
                    Value = temp,
                    TimestampLocal = DateTime.Now
                });
            }

            if (parts.Length >= 3 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var load))
            {
                _snapshot.AllHardwareSensors.Add(new SensorReading
                {
                    Name = $"{gpuName} / GPU Core Load",
                    HardwareGroup = "Gpu",
                    Kind = SensorKind.Load,
                    Unit = "%",
                    Value = load,
                    TimestampLocal = DateTime.Now
                });
            }
        }
    }

    private void PopulateFromLmSensors()
    {
        var output = TryRun("sensors", "-u");
        if (string.IsNullOrWhiteSpace(output))
            return;

        string currentChip = "Motherboard";
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (!char.IsWhiteSpace(raw, 0))
            {
                currentChip = line.Trim();
                continue;
            }

            var t = line.Trim();
            var sep = t.IndexOf(':');
            if (sep <= 0)
                continue;
            var key = t[..sep].Trim();
            var valText = t[(sep + 1)..].Trim();
            if (!double.TryParse(valText, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                continue;

            var (kind, unit) = InferKindUnit(key);
            var group = InferGroup(currentChip);
            var sr = new SensorReading
            {
                Name = $"{currentChip} / {key}",
                HardwareGroup = group,
                Kind = kind,
                Unit = unit,
                Value = v,
                TimestampLocal = DateTime.Now
            };
            _snapshot.AllHardwareSensors.Add(sr);
        }
    }

    private static (SensorKind Kind, string Unit) InferKindUnit(string key)
    {
        if (key.Contains("temp", StringComparison.OrdinalIgnoreCase))
            return (SensorKind.Temperature, "°C");
        if (key.Contains("fan", StringComparison.OrdinalIgnoreCase))
            return (SensorKind.Fan, "RPM");
        if (key.Contains("in", StringComparison.OrdinalIgnoreCase) || key.Contains("volt", StringComparison.OrdinalIgnoreCase))
            return (SensorKind.Voltage, "V");
        if (key.Contains("power", StringComparison.OrdinalIgnoreCase))
            return (SensorKind.Power, "W");
        if (key.Contains("freq", StringComparison.OrdinalIgnoreCase) || key.Contains("clock", StringComparison.OrdinalIgnoreCase))
            return (SensorKind.Clock, "MHz");
        return (SensorKind.Other, "");
    }

    private static string InferGroup(string chip)
    {
        if (chip.Contains("coretemp", StringComparison.OrdinalIgnoreCase) || chip.Contains("k10temp", StringComparison.OrdinalIgnoreCase))
            return "Cpu";
        if (chip.Contains("nvme", StringComparison.OrdinalIgnoreCase) || chip.Contains("drivetemp", StringComparison.OrdinalIgnoreCase))
            return "Storage";
        return "Motherboard";
    }

    private static string ReadCpuModelName()
    {
        try
        {
            var text = File.ReadAllLines("/proc/cpuinfo");
            var line = text.FirstOrDefault(x => x.StartsWith("model name", StringComparison.OrdinalIgnoreCase));
            if (line is null) return "Unknown CPU";
            var i = line.IndexOf(':');
            return i > 0 ? line[(i + 1)..].Trim() : "Unknown CPU";
        }
        catch
        {
            return "Unknown CPU";
        }
    }

    private void ReadMemoryFromProc()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/meminfo");
            long totalKb = ParseMemKb(lines, "MemTotal");
            long availableKb = ParseMemKb(lines, "MemAvailable");
            _snapshot.TotalRamBytes = totalKb * 1024;
            _snapshot.FreeRamBytes = availableKb * 1024;
        }
        catch
        {
            _snapshot.TotalRamBytes = 0;
            _snapshot.FreeRamBytes = 0;
        }
    }

    private static long ParseMemKb(IEnumerable<string> lines, string key)
    {
        var line = lines.FirstOrDefault(x => x.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
        if (line is null) return 0;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var v) ? v : 0;
    }

    private double? ReadCpuLoadFromProcStat()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault();
            if (line is null || !line.StartsWith("cpu ")) return null;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            if (p.Length < 4) return null;
            long user = long.Parse(p[0], CultureInfo.InvariantCulture);
            long nice = long.Parse(p[1], CultureInfo.InvariantCulture);
            long system = long.Parse(p[2], CultureInfo.InvariantCulture);
            long idle = long.Parse(p[3], CultureInfo.InvariantCulture);
            long iowait = p.Length > 4 ? long.Parse(p[4], CultureInfo.InvariantCulture) : 0;
            long total = user + nice + system + idle + iowait;
            long idleTotal = idle + iowait;
            if (_prevCpuReadUtc == DateTime.MinValue)
            {
                _prevCpuReadUtc = DateTime.UtcNow;
                _prevTotalTicks = total;
                _prevIdleTicks = idleTotal;
                return null;
            }

            var totalDelta = total - _prevTotalTicks;
            var idleDelta = idleTotal - _prevIdleTicks;
            _prevTotalTicks = total;
            _prevIdleTicks = idleTotal;
            _prevCpuReadUtc = DateTime.UtcNow;
            if (totalDelta <= 0) return null;
            var load = 100.0 * (1.0 - (double)idleDelta / totalDelta);
            return Math.Clamp(load, 0, 100);
        }
        catch
        {
            return null;
        }
    }

    private double? ReadCpuTemperature()
    {
        try
        {
            double? best = null;
            foreach (var hwmonDir in Directory.EnumerateDirectories("/sys/class/hwmon"))
            {
                var namePath = Path.Combine(hwmonDir, "name");
                if (!File.Exists(namePath))
                    continue;
                var chip = File.ReadAllText(namePath).Trim();
                if (!chip.Contains("coretemp", StringComparison.OrdinalIgnoreCase) &&
                    !chip.Contains("k10temp", StringComparison.OrdinalIgnoreCase) &&
                    !chip.Contains("zenpower", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var input in Directory.EnumerateFiles(hwmonDir, "temp*_input"))
                {
                    if (!double.TryParse(File.ReadAllText(input).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
                        continue;
                    var c = raw > 500 ? raw / 1000.0 : raw;
                    if (c is > -20 and < 150)
                        best = best is null ? c : Math.Max(best.Value, c);
                }
            }

            if (best is not null)
                return best;

            foreach (var f in Directory.EnumerateFiles("/sys/class/thermal", "temp", SearchOption.AllDirectories).Take(48))
            {
                var typePath = Path.Combine(Path.GetDirectoryName(f)!, "type");
                var type = File.Exists(typePath) ? File.ReadAllText(typePath).Trim() : "";
                if (!type.Contains("cpu", StringComparison.OrdinalIgnoreCase) &&
                    !type.Contains("x86", StringComparison.OrdinalIgnoreCase) &&
                    !type.Contains("proc", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!double.TryParse(File.ReadAllText(f).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
                    continue;
                var c = raw > 500 ? raw / 1000.0 : raw;
                if (c is > -20 and < 150)
                    return c;
            }
        }
        catch
        {
        }

        return null;
    }

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
            p.WaitForExit(1500);
            if (p.ExitCode != 0) return null;
            return output;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
    }
}
