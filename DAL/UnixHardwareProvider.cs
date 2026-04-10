using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using PC_HealthCheck.Core;

namespace PC_HealthCheck.DAL;

/// <summary>
/// Ограниченный кроссплатформенный провайдер для Linux/macOS.
/// Без привилегированного доступа к низкоуровневым датчикам возвращает только базовые метрики.
/// </summary>
public sealed class UnixHardwareProvider : IHardwareProvider
{
    private DeviceSnapshot _snapshot = new();

    public bool IsInitialized { get; private set; }

    public Task<bool> InitializeAsync()
    {
        var os = RuntimeInformation.OSDescription;
        var arch = RuntimeInformation.ProcessArchitecture.ToString();
        _snapshot = new DeviceSnapshot
        {
            CpuName = RuntimeInformation.OSArchitecture + " CPU",
            CpuManufacturer = "Unknown",
            CpuInstructionSets = "N/A (non-WMI provider)",
            MotherboardModel = "Unknown",
            MotherboardManufacturer = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Apple" : "Unknown",
            BiosVersion = RuntimeInformation.FrameworkDescription,
            BiosDate = "",
            RamType = "Unknown"
        };

        _snapshot.CpuMicroarchitectureHint = $"{os}, {arch}";
        IsInitialized = true;
        return Task.FromResult(true);
    }

    public Task<DeviceSnapshot> ReadSnapshotAsync()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Provider is not initialized.");

        _snapshot.TimestampUtc = DateTime.UtcNow;
        _snapshot.CpuTemperatureC = null;
        _snapshot.CpuLoadPercent = null;
        _snapshot.CpuSensors.Clear();
        _snapshot.AllHardwareSensors.Clear();
        _snapshot.CpuPerCoreClocks.Clear();
        _snapshot.CpuPerCoreVoltages.Clear();
        _snapshot.VrmChipsetSensors.Clear();
        _snapshot.FanSensors.Clear();
        _snapshot.StorageTemperatureSensors.Clear();
        _snapshot.MemoryModules.Clear();
        _snapshot.MemoryArrays.Clear();
        _snapshot.MemorySlots.Clear();
        _snapshot.Gpus.Clear();
        _snapshot.PciDevices.Clear();
        _snapshot.Peripherals.Clear();
        _snapshot.Printers.Clear();

        // Без root/спец-библиотек берем только логические диски и сетевые интерфейсы.
        _snapshot.LogicalDisks = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new LogicalDiskInfo
            {
                DeviceId = d.Name,
                VolumeName = d.VolumeLabel,
                FileSystem = d.DriveFormat,
                SizeBytes = d.TotalSize,
                FreeBytes = d.AvailableFreeSpace
            })
            .ToList();

        _snapshot.NetworkAdapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => new NetworkAdapterInfo
            {
                Name = n.Name,
                Manufacturer = "",
                MacAddress = n.GetPhysicalAddress().ToString(),
                NetEnabled = true,
                NetConnectionId = n.Description,
                SpeedBitsPerSec = n.Speed > 0 ? (ulong)n.Speed : 0,
                AdapterType = n.NetworkInterfaceType.ToString()
            })
            .ToList();

        return Task.FromResult(CloneSnapshot(_snapshot));
    }

    private static DeviceSnapshot CloneSnapshot(DeviceSnapshot s)
    {
        return new DeviceSnapshot
        {
            TimestampUtc = s.TimestampUtc,
            CpuName = s.CpuName,
            CpuManufacturer = s.CpuManufacturer,
            CpuCores = s.CpuCores,
            CpuThreads = s.CpuThreads,
            CpuMaxClockMHz = s.CpuMaxClockMHz,
            CpuFamily = s.CpuFamily,
            CpuModel = s.CpuModel,
            CpuStepping = s.CpuStepping,
            CpuSignatureRaw = s.CpuSignatureRaw,
            CpuMicroarchitectureHint = s.CpuMicroarchitectureHint,
            CpuInstructionSets = s.CpuInstructionSets,
            CpuTemperatureC = s.CpuTemperatureC,
            CpuLoadPercent = s.CpuLoadPercent,
            TotalRamBytes = s.TotalRamBytes,
            FreeRamBytes = s.FreeRamBytes,
            RamType = s.RamType,
            MotherboardManufacturer = s.MotherboardManufacturer,
            MotherboardModel = s.MotherboardModel,
            BiosVersion = s.BiosVersion,
            BiosDate = s.BiosDate,
            LogicalDisks = s.LogicalDisks.ToList(),
            NetworkAdapters = s.NetworkAdapters.ToList()
        };
    }

    public void Dispose()
    {
    }
}
