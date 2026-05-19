using PC_HealthCheck.Core;

namespace PC_HealthCheck.DAL;

internal static class DeviceSnapshotCloner
{
    public static DeviceSnapshot Clone(DeviceSnapshot s) => new()
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
        MotherboardModel = s.MotherboardModel,
        MotherboardManufacturer = s.MotherboardManufacturer,
        BiosVersion = s.BiosVersion,
        BiosDate = s.BiosDate,
        CpuSensors = s.CpuSensors.Select(CloneSensor).ToList(),
        AllHardwareSensors = s.AllHardwareSensors.Select(CloneSensor).ToList(),
        MemoryModules = s.MemoryModules.Select(m => new MemoryModuleInfo
        {
            BankLabel = m.BankLabel,
            DeviceLocator = m.DeviceLocator,
            Manufacturer = m.Manufacturer,
            PartNumber = m.PartNumber,
            SerialNumber = m.SerialNumber,
            CapacityBytes = m.CapacityBytes,
            SpeedMHz = m.SpeedMHz,
            FormFactor = m.FormFactor,
            TypeDetail = m.TypeDetail,
            ConfiguredClockSpeedMHz = m.ConfiguredClockSpeedMHz,
            MinVoltageMilliVolts = m.MinVoltageMilliVolts,
            MaxVoltageMilliVolts = m.MaxVoltageMilliVolts,
            ConfiguredVoltageMilliVolts = m.ConfiguredVoltageMilliVolts,
            AttributesRaw = m.AttributesRaw
        }).ToList(),
        MemoryArrays = s.MemoryArrays.Select(a => new MemoryArrayInfo
        {
            Use = a.Use,
            MaxCapacityBytes = a.MaxCapacityBytes,
            MemoryDevices = a.MemoryDevices,
            ErrorCorrection = a.ErrorCorrection
        }).ToList(),
        MemorySlots = s.MemorySlots.Select(x => new MemorySlotInfo
        {
            DeviceLocator = x.DeviceLocator,
            BankLabel = x.BankLabel,
            CapacityBytes = x.CapacityBytes,
            SpeedMHz = x.SpeedMHz,
            FormFactor = x.FormFactor,
            MemoryType = x.MemoryType,
            TypeDetail = x.TypeDetail
        }).ToList(),
        Gpus = s.Gpus.Select(g => new GpuInfo
        {
            Name = g.Name,
            Manufacturer = g.Manufacturer,
            DriverVersion = g.DriverVersion,
            VramBytes = g.VramBytes
        }).ToList(),
        PciDevices = s.PciDevices.Select(p => new PciDeviceInfo
        {
            Name = p.Name,
            Manufacturer = p.Manufacturer,
            PnpDeviceId = p.PnpDeviceId,
            DeviceId = p.DeviceId,
            PnpClass = p.PnpClass
        }).ToList(),
        NetworkAdapters = s.NetworkAdapters.Select(n => new NetworkAdapterInfo
        {
            Name = n.Name,
            Manufacturer = n.Manufacturer,
            MacAddress = n.MacAddress,
            NetEnabled = n.NetEnabled,
            NetConnectionId = n.NetConnectionId,
            SpeedBitsPerSec = n.SpeedBitsPerSec,
            AdapterType = n.AdapterType
        }).ToList(),
        Peripherals = s.Peripherals.Select(p => new PeripheralInfo
        {
            Category = p.Category,
            Name = p.Name,
            Manufacturer = p.Manufacturer,
            DeviceId = p.DeviceId
        }).ToList(),
        Printers = s.Printers.Select(p => new PrinterInfo
        {
            Name = p.Name,
            DriverName = p.DriverName,
            PortName = p.PortName,
            Default = p.Default,
            Network = p.Network
        }).ToList(),
        PhysicalDisks = s.PhysicalDisks.Select(d => new PhysicalDiskInfo
        {
            DeviceId = d.DeviceId,
            PnpDeviceId = d.PnpDeviceId,
            Model = d.Model,
            SerialNumber = d.SerialNumber,
            InterfaceType = d.InterfaceType,
            MediaType = d.MediaType,
            SizeBytes = d.SizeBytes,
            Partitions = d.Partitions,
            SmartPredictFailure = d.SmartPredictFailure,
            SmartReason = d.SmartReason,
            SmartAttributes = d.SmartAttributes.Select(a => new SmartAttributeInfo
            {
                Id = a.Id,
                Name = a.Name,
                Flags = a.Flags,
                Current = a.Current,
                Worst = a.Worst,
                Threshold = a.Threshold,
                RawValue = a.RawValue
            }).ToList()
        }).ToList(),
        LogicalDisks = s.LogicalDisks.Select(l => new LogicalDiskInfo
        {
            DeviceId = l.DeviceId,
            VolumeName = l.VolumeName,
            FileSystem = l.FileSystem,
            SizeBytes = l.SizeBytes,
            FreeBytes = l.FreeBytes
        }).ToList(),
        CpuPerCoreClocks = s.CpuPerCoreClocks.Select(CloneSensor).ToList(),
        CpuPerCoreVoltages = s.CpuPerCoreVoltages.Select(CloneSensor).ToList(),
        VrmChipsetSensors = s.VrmChipsetSensors.Select(CloneSensor).ToList(),
        FanSensors = s.FanSensors.Select(CloneSensor).ToList(),
        StorageTemperatureSensors = s.StorageTemperatureSensors.Select(CloneSensor).ToList(),
        RamTimingSummaryLines = s.RamTimingSummaryLines.ToList()
    };

    private static SensorReading CloneSensor(SensorReading r) => new()
    {
        Name = r.Name,
        HardwareGroup = r.HardwareGroup,
        Kind = r.Kind,
        Unit = r.Unit,
        Value = r.Value,
        TimestampLocal = r.TimestampLocal
    };
}
