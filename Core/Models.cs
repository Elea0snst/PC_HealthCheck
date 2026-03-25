namespace PC_HealthCheck.Core;



public enum SensorKind

{

    Temperature,

    Load,

    Clock,

    Voltage

}



public sealed class SensorReading

{

    public string Name { get; set; } = "";

    public SensorKind Kind { get; set; }

    public string Unit { get; set; } = "";

    public double Value { get; set; }

    public DateTime TimestampLocal { get; set; } = DateTime.Now;

}



public sealed class MemoryModuleInfo

{

    public string BankLabel { get; set; } = "";

    public string DeviceLocator { get; set; } = "";

    public string Manufacturer { get; set; } = "";

    public string PartNumber { get; set; } = "";

    public string SerialNumber { get; set; } = "";

    public long CapacityBytes { get; set; }

    public uint SpeedMHz { get; set; }

    public string FormFactor { get; set; } = "";

    public string TypeDetail { get; set; } = "";

}



/// <summary>Physical memory array (SMBIOS) — slot count, max capacity, ECC.</summary>

public sealed class MemoryArrayInfo

{

    public string Use { get; set; } = "";

    public long MaxCapacityBytes { get; set; }

    public int MemoryDevices { get; set; }

    public string ErrorCorrection { get; set; } = "";

}



/// <summary>Per-slot view from Win32_MemoryDevice (includes empty slots when WMI exposes them).</summary>

public sealed class MemorySlotInfo

{

    public string DeviceLocator { get; set; } = "";

    public string BankLabel { get; set; } = "";

    public long? CapacityBytes { get; set; }

    public uint SpeedMHz { get; set; }

    public string FormFactor { get; set; } = "";

    public string MemoryType { get; set; } = "";

    public string TypeDetail { get; set; } = "";

}



public sealed class GpuInfo

{

    public string Name { get; set; } = "";

    public string Manufacturer { get; set; } = "";

    public string DriverVersion { get; set; } = "";

    public long VramBytes { get; set; }

}



public sealed class PciDeviceInfo

{

    public string Name { get; set; } = "";

    public string Manufacturer { get; set; } = "";

    public string PnpDeviceId { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string PnpClass { get; set; } = "";

}



public sealed class NetworkAdapterInfo

{

    public string Name { get; set; } = "";

    public string Manufacturer { get; set; } = "";

    public string MacAddress { get; set; } = "";

    public bool NetEnabled { get; set; }

    public string NetConnectionId { get; set; } = "";

    public ulong SpeedBitsPerSec { get; set; }

    public string AdapterType { get; set; } = "";

}



public sealed class PeripheralInfo

{

    public string Category { get; set; } = "";

    public string Name { get; set; } = "";

    public string Manufacturer { get; set; } = "";

    public string DeviceId { get; set; } = "";

}



public sealed class PrinterInfo

{

    public string Name { get; set; } = "";

    public string DriverName { get; set; } = "";

    public string PortName { get; set; } = "";

    public bool Default { get; set; }

    public bool Network { get; set; }

}



public sealed class LogicalDiskInfo

{

    public string DeviceId { get; set; } = "";

    public string VolumeName { get; set; } = "";

    public string FileSystem { get; set; } = "";

    public long SizeBytes { get; set; }

    public long FreeBytes { get; set; }

}



public sealed class SmartAttributeInfo

{

    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int Flags { get; set; }

    public byte Current { get; set; }

    public byte Worst { get; set; }

    public byte Threshold { get; set; }

    public ulong RawValue { get; set; }

}



public sealed class PhysicalDiskInfo

{

    public string DeviceId { get; set; } = "";

    public string PnpDeviceId { get; set; } = "";

    public string Model { get; set; } = "";

    public string SerialNumber { get; set; } = "";

    public string InterfaceType { get; set; } = "";

    public string MediaType { get; set; } = "";

    public long SizeBytes { get; set; }

    public int Partitions { get; set; }

    public bool? SmartPredictFailure { get; set; }

    public string SmartReason { get; set; } = "";

    public List<SmartAttributeInfo> SmartAttributes { get; set; } = new();

}



public sealed class DeviceSnapshot

{

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public DateTime TimestampLocal => TimestampUtc.ToLocalTime();



    public string CpuName { get; set; } = "";

    public string CpuManufacturer { get; set; } = "";

    public int CpuCores { get; set; }

    public int CpuThreads { get; set; }

    public uint CpuMaxClockMHz { get; set; }



    public uint CpuFamily { get; set; }

    public uint CpuModel { get; set; }

    public uint CpuStepping { get; set; }

    public string CpuSignatureRaw { get; set; } = "";

    public string CpuMicroarchitectureHint { get; set; } = "";

    public string CpuInstructionSets { get; set; } = "";



    public double? CpuTemperatureC { get; set; }

    public double? CpuLoadPercent { get; set; }



    public long TotalRamBytes { get; set; }

    public long FreeRamBytes { get; set; }

    public string RamType { get; set; } = "";

    public double RamUsagePercent =>

        TotalRamBytes <= 0 ? 0 : (1.0 - (double)FreeRamBytes / TotalRamBytes) * 100.0;



    public string MotherboardModel { get; set; } = "";

    public string MotherboardManufacturer { get; set; } = "";

    public string BiosVersion { get; set; } = "";

    public string BiosDate { get; set; } = "";



    public List<SensorReading> CpuSensors { get; set; } = new();

    public List<MemoryModuleInfo> MemoryModules { get; set; } = new();

    public List<MemoryArrayInfo> MemoryArrays { get; set; } = new();

    public List<MemorySlotInfo> MemorySlots { get; set; } = new();

    public List<GpuInfo> Gpus { get; set; } = new();

    public List<PciDeviceInfo> PciDevices { get; set; } = new();

    public List<NetworkAdapterInfo> NetworkAdapters { get; set; } = new();

    public List<PeripheralInfo> Peripherals { get; set; } = new();

    public List<PrinterInfo> Printers { get; set; } = new();

    public List<PhysicalDiskInfo> PhysicalDisks { get; set; } = new();

    public List<LogicalDiskInfo> LogicalDisks { get; set; } = new();

}



public enum TestStatus

{

    NotStarted,

    Running,

    Completed,

    Cancelled,

    Failed

}



public sealed class StressResult

{

    public string TestName { get; set; } = "";

    public TestStatus Status { get; set; }

    public string Error { get; set; } = "";

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    public DateTime EndedUtc { get; set; } = DateTime.UtcNow;

}


