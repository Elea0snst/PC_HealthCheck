using LibreHardwareMonitor.Hardware;

using PC_HealthCheck.Core;

using System.Management;

using System.Text.RegularExpressions;



namespace PC_HealthCheck.DAL;



public sealed class WindowsHardwareProvider : IHardwareProvider

{

    private Computer? _computer;

    private readonly object _lock = new();

    private DeviceSnapshot _snapshot = new();



    public bool IsInitialized { get; private set; }



    public async Task<bool> InitializeAsync()

    {

        try

        {

            await Task.Run(LoadStaticInfoFromWmi);



            _computer = new Computer

            {

                IsCpuEnabled = true,

                IsGpuEnabled = true,

                IsMotherboardEnabled = true,

                IsMemoryEnabled = true,

                IsStorageEnabled = true,

                IsControllerEnabled = true

            };

            _computer.Open();



            IsInitialized = true;

            return true;

        }

        catch

        {

            IsInitialized = false;

            return false;

        }

    }



    public async Task<DeviceSnapshot> ReadSnapshotAsync()

    {

        if (!IsInitialized)

            throw new InvalidOperationException("Provider is not initialized.");



        return await Task.Run(() =>

        {

            lock (_lock)

            {

                _snapshot.TimestampUtc = DateTime.UtcNow;



                UpdateCpuAndGpuSensors();

                UpdateDynamicMemoryFromWmi();

                UpdateLogicalDisksFromWmi();

                UpdateNetworkAdaptersFromWmi();

                TryRefreshSmartStatus();



                return CloneSnapshot(_snapshot);

            }

        });

    }



    private void LoadStaticInfoFromWmi()

    {

        lock (_lock)

        {

            _snapshot.MemoryModules.Clear();

            _snapshot.RamTimingSummaryLines.Clear();

            _snapshot.MemoryArrays.Clear();

            _snapshot.MemorySlots.Clear();

            _snapshot.Gpus.Clear();

            _snapshot.PciDevices.Clear();

            _snapshot.Peripherals.Clear();

            _snapshot.Printers.Clear();

            _snapshot.PhysicalDisks.Clear();

            _snapshot.LogicalDisks.Clear();

            _snapshot.NetworkAdapters.Clear();



            _snapshot.TotalRamBytes = 0;

            _snapshot.RamType = "";



            try

            {

                using (var cpuSearcher = new ManagementObjectSearcher(

                           @"SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed,

                                Family, Stepping, Revision, ProcessorId

                         FROM Win32_Processor"))

                {

                    foreach (ManagementObject obj in cpuSearcher.Get())

                    {

                        _snapshot.CpuName = obj["Name"]?.ToString() ?? "Unknown";

                        _snapshot.CpuManufacturer = obj["Manufacturer"]?.ToString() ?? "Unknown";

                        _snapshot.CpuCores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);

                        _snapshot.CpuThreads = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0);

                        _snapshot.CpuMaxClockMHz = Convert.ToUInt32(obj["MaxClockSpeed"] ?? 0);



                        var procId = obj["ProcessorId"]?.ToString();

                        var (fam, mod, step, rawHex) = CpuSignatureDecoder.FromProcessorIdHex(procId);

                        if (fam == 0 && mod == 0 && step == 0)

                        {

                            fam = ToUInt(obj["Family"]);

                            mod = 0;

                            step = ToUInt(obj["Stepping"]);

                        }



                        _snapshot.CpuFamily = fam;

                        _snapshot.CpuModel = mod;

                        _snapshot.CpuStepping = step;

                        _snapshot.CpuSignatureRaw = rawHex.Length > 0 ? rawHex : procId ?? "";

                        _snapshot.CpuMicroarchitectureHint =

                            CpuSignatureDecoder.DescribeMicroarchitecture(_snapshot.CpuManufacturer, fam, mod);

                        _snapshot.CpuInstructionSets = CpuFeatureDetector.SummarizeX86();

                        break;

                    }

                }

            }

            catch (Exception)

            {

                // Неверный WQL-синтаксис, отсутствующие поля в Win32_Processor на этой ОС, отказ WMI/COM и т.д.

            }



            using (var boardSearcher = new ManagementObjectSearcher(

                       "SELECT Product, Manufacturer FROM Win32_BaseBoard"))

            {

                foreach (ManagementObject obj in boardSearcher.Get())

                {

                    _snapshot.MotherboardModel = obj["Product"]?.ToString() ?? "";

                    _snapshot.MotherboardManufacturer = obj["Manufacturer"]?.ToString() ?? "";

                    break;

                }

            }



            using (var biosSearcher = new ManagementObjectSearcher(

                       "SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))

            {

                foreach (ManagementObject obj in biosSearcher.Get())

                {

                    _snapshot.BiosVersion = obj["SMBIOSBIOSVersion"]?.ToString() ?? "";

                    _snapshot.BiosDate = obj["ReleaseDate"]?.ToString() ?? "";

                    break;

                }

            }



            TryLoadPhysicalMemoryModules();



            TryLoadMemoryArrayAndSlots();

            LoadPciDevices();

            LoadPeripheralsAndPrinters();

            LoadPhysicalDisksAndSmart();



            using (var gpuSearcher = new ManagementObjectSearcher(

                       "SELECT Name, AdapterCompatibility, DriverVersion, AdapterRAM FROM Win32_VideoController"))

            {

                foreach (ManagementObject obj in gpuSearcher.Get())

                {

                    _snapshot.Gpus.Add(new GpuInfo

                    {

                        Name = obj["Name"]?.ToString() ?? "Unknown GPU",

                        Manufacturer = obj["AdapterCompatibility"]?.ToString() ?? "Unknown",

                        DriverVersion = obj["DriverVersion"]?.ToString() ?? "",

                        VramBytes = Convert.ToInt64(obj["AdapterRAM"] ?? 0L)

                    });

                }

            }



            UpdateLogicalDisksFromWmi();

            UpdateNetworkAdaptersFromWmi();

        }

    }



    private static uint ToUInt(object? o)

    {

        if (o is null) return 0;

        try

        {

            return Convert.ToUInt32(o);

        }

        catch

        {

            return 0;

        }

    }



    private void TryLoadPhysicalMemoryModules()

    {

        var extendedWql = @"SELECT Capacity, Speed, Manufacturer, PartNumber, SerialNumber, SMBIOSMemoryType,

                                BankLabel, DeviceLocator, FormFactor, TypeDetail,

                                ConfiguredClockSpeed, ConfiguredVoltage, MinVoltage, MaxVoltage, Attributes

                         FROM Win32_PhysicalMemory";

        var basicWql = @"SELECT Capacity, Speed, Manufacturer, PartNumber, SerialNumber, SMBIOSMemoryType,

                                BankLabel, DeviceLocator, FormFactor, TypeDetail

                         FROM Win32_PhysicalMemory";



        foreach (var (wql, extended) in new[] { (extendedWql, true), (basicWql, false) })

        {

            try

            {

                _snapshot.MemoryModules.Clear();

                long sumBytes = 0;

                using var ramSearcher = new ManagementObjectSearcher(wql);

                foreach (ManagementObject obj in ramSearcher.Get())

                {

                    var cap = Convert.ToInt64(obj["Capacity"] ?? 0L);

                    sumBytes += cap;

                    if (_snapshot.RamType.Length == 0)

                        _snapshot.RamType = DecodeMemoryType(Convert.ToInt32(obj["SMBIOSMemoryType"] ?? 0));



                    var m = new MemoryModuleInfo

                    {

                        BankLabel = obj["BankLabel"]?.ToString() ?? "",

                        DeviceLocator = obj["DeviceLocator"]?.ToString() ?? "",

                        Manufacturer = obj["Manufacturer"]?.ToString() ?? "",

                        PartNumber = obj["PartNumber"]?.ToString()?.Trim() ?? "",

                        SerialNumber = obj["SerialNumber"]?.ToString() ?? "",

                        CapacityBytes = cap,

                        SpeedMHz = Convert.ToUInt32(obj["Speed"] ?? 0),

                        FormFactor = DecodeFormFactor(Convert.ToUInt32(obj["FormFactor"] ?? 0u)),

                        TypeDetail = DecodeTypeDetail(Convert.ToUInt32(obj["TypeDetail"] ?? 0u))

                    };



                    if (extended)

                    {

                        m.ConfiguredClockSpeedMHz = Convert.ToUInt32(obj["ConfiguredClockSpeed"] ?? 0u);

                        m.MinVoltageMilliVolts = ToUInt(obj["MinVoltage"]);

                        m.MaxVoltageMilliVolts = ToUInt(obj["MaxVoltage"]);

                        m.ConfiguredVoltageMilliVolts = ToUInt(obj["ConfiguredVoltage"]);

                        m.AttributesRaw = ToUInt(obj["Attributes"]);

                    }



                    _snapshot.MemoryModules.Add(m);

                }



                _snapshot.TotalRamBytes = sumBytes;

                FillRamTimingSummaryLines();

                if (_snapshot.MemoryModules.Count > 0 || !extended)
                    return;

            }

            catch (ManagementException)

            {

                // fallback WQL

            }

            catch (System.Runtime.InteropServices.COMException)

            {

            }

        }



        FillRamTimingSummaryLines();

    }



    private void FillRamTimingSummaryLines()

    {

        _snapshot.RamTimingSummaryLines.Clear();

        if (_snapshot.MemoryModules.Count == 0)

        {

            _snapshot.RamTimingSummaryLines.Add("Модули RAM (Win32_PhysicalMemory): не удалось получить данные WMI.");

            return;

        }



        _snapshot.RamTimingSummaryLines.Add(

            "Тайминги из SPD (tCL, tRCD, tRP, tRAS, XMP/EXPO) через WMI недоступны — нужен доступ к SMBus/SPD. " +

            "Ниже: номинальная и сконфигурированная частота и напряжения из Win32_PhysicalMemory (если отдаёт BIOS).");



        foreach (var m in _snapshot.MemoryModules)

        {

            var cfg = m.ConfiguredClockSpeedMHz > 0 ? $"{m.ConfiguredClockSpeedMHz} МГц" : "н/д";

            string Mv(uint v) => v > 0 ? $"{v} мВ" : "н/д";

            _snapshot.RamTimingSummaryLines.Add(

                $"{m.DeviceLocator} ({m.BankLabel}): JEDEC/номинал {m.SpeedMHz} МГц, сконфиг. {cfg}; " +

                $"U: {Mv(m.ConfiguredVoltageMilliVolts)}, min {Mv(m.MinVoltageMilliVolts)}, max {Mv(m.MaxVoltageMilliVolts)}; attr 0x{m.AttributesRaw:X4}");

        }

    }



    private void TryLoadMemoryArrayAndSlots()

    {

        try

        {

            using var arrSearcher = new ManagementObjectSearcher(

                "SELECT Use, MaxCapacity, MemoryDevices, MemoryErrorCorrection FROM Win32_PhysicalMemoryArray");

            foreach (ManagementObject obj in arrSearcher.Get())

            {

                _snapshot.MemoryArrays.Add(new MemoryArrayInfo

                {

                    Use = obj["Use"]?.ToString() ?? "",

                    MaxCapacityBytes = Convert.ToInt64(obj["MaxCapacity"] ?? 0L),

                    MemoryDevices = Convert.ToInt32(obj["MemoryDevices"] ?? 0),

                    ErrorCorrection = obj["MemoryErrorCorrection"]?.ToString() ?? ""

                });

            }

        }

        catch

        {

            // Older hosts may lack the class

        }



        try

        {

            using var devSearcher = new ManagementObjectSearcher(

                @"SELECT BankLabel, Capacity, DataWidth, DeviceLocator, FormFactor, InterleavePosition,

                         MemoryType, SMBIOSMemoryType, Speed, TotalWidth, TypeDetail

                  FROM Win32_MemoryDevice");

            foreach (ManagementObject obj in devSearcher.Get())

            {

                long? cap = null;

                var c = obj["Capacity"];

                if (c != null && !string.IsNullOrEmpty(c.ToString()))

                {

                    var v = Convert.ToInt64(c);

                    if (v > 0) cap = v;

                }



                _snapshot.MemorySlots.Add(new MemorySlotInfo

                {

                    BankLabel = obj["BankLabel"]?.ToString() ?? "",

                    DeviceLocator = obj["DeviceLocator"]?.ToString() ?? "",

                    CapacityBytes = cap,

                    SpeedMHz = Convert.ToUInt32(obj["Speed"] ?? 0u),

                    FormFactor = DecodeFormFactor(Convert.ToUInt32(obj["FormFactor"] ?? 0u)),

                    MemoryType = DecodeMemoryType(Convert.ToInt32(obj["SMBIOSMemoryType"] ?? 0)),

                    TypeDetail = DecodeTypeDetail(Convert.ToUInt32(obj["TypeDetail"] ?? 0u))

                });

            }

        }

        catch

        {

            // Not available on all Windows builds

        }

    }



    private void LoadPciDevices()

    {

        const int maxRows = 180;

        try

        {

            using var searcher = new ManagementObjectSearcher(

                @"SELECT Name, Manufacturer, PNPDeviceID, DeviceID, PNPClass

                  FROM Win32_PnPEntity

                  WHERE PNPDeviceID LIKE 'PCI\\%'");

            var n = 0;

            foreach (ManagementObject obj in searcher.Get())

            {

                if (n++ >= maxRows) break;

                _snapshot.PciDevices.Add(new PciDeviceInfo

                {

                    Name = obj["Name"]?.ToString() ?? "",

                    Manufacturer = obj["Manufacturer"]?.ToString() ?? "",

                    PnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "",

                    DeviceId = obj["DeviceID"]?.ToString() ?? "",

                    PnpClass = obj["PNPClass"]?.ToString() ?? ""

                });

            }

        }

        catch

        {

            // ignore

        }

    }



    private void LoadPeripheralsAndPrinters()

    {

        void addCategory(string category, string wql)

        {

            try

            {

                using var s = new ManagementObjectSearcher(wql);

                foreach (ManagementObject obj in s.Get())

                {

                    _snapshot.Peripherals.Add(new PeripheralInfo

                    {

                        Category = category,

                        Name = obj["Name"]?.ToString() ?? "",

                        Manufacturer = obj["Manufacturer"]?.ToString() ?? "",

                        DeviceId = obj["DeviceID"]?.ToString() ?? ""

                    });

                }

            }

            catch

            {

                // ignore

            }

        }



        addCategory("Keyboard", "SELECT Name, Manufacturer, DeviceID FROM Win32_Keyboard");

        addCategory("Pointing", "SELECT Name, Manufacturer, DeviceID FROM Win32_PointingDevice");

        addCategory("Sound", "SELECT Name, Manufacturer, DeviceID FROM Win32_SoundDevice");

        addCategory("USB controller", "SELECT Name, Manufacturer, DeviceID FROM Win32_USBController");



        try

        {

            using var ps = new ManagementObjectSearcher(

                "SELECT Name, DriverName, PortName, Default, Network FROM Win32_Printer");

            foreach (ManagementObject obj in ps.Get())

            {

                _snapshot.Printers.Add(new PrinterInfo

                {

                    Name = obj["Name"]?.ToString() ?? "",

                    DriverName = obj["DriverName"]?.ToString() ?? "",

                    PortName = obj["PortName"]?.ToString() ?? "",

                    Default = Convert.ToBoolean(obj["Default"] ?? false),

                    Network = Convert.ToBoolean(obj["Network"] ?? false)

                });

            }

        }

        catch

        {

            // ignore

        }

    }



    private void LoadPhysicalDisksAndSmart()

    {

        try

        {

            using var s = new ManagementObjectSearcher(

                @"SELECT DeviceID, Model, SerialNumber, InterfaceType, MediaType, Size, Partitions, PNPDeviceID

                  FROM Win32_DiskDrive");

            foreach (ManagementObject obj in s.Get())

            {

                _snapshot.PhysicalDisks.Add(new PhysicalDiskInfo

                {

                    DeviceId = obj["DeviceID"]?.ToString() ?? "",

                    Model = (obj["Model"]?.ToString() ?? "").Trim(),

                    SerialNumber = (obj["SerialNumber"]?.ToString() ?? "").Trim(),

                    InterfaceType = obj["InterfaceType"]?.ToString() ?? "",

                    MediaType = obj["MediaType"]?.ToString() ?? "",

                    SizeBytes = Convert.ToInt64(obj["Size"] ?? 0L),

                    Partitions = Convert.ToInt32(obj["Partitions"] ?? 0),

                    PnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? ""

                });

            }

        }

        catch

        {

            // ignore

        }



        TryAttachSmartData();

    }



    private void TryRefreshSmartStatus()

    {

        TryAttachSmartData();

    }



    private void TryAttachSmartData()

    {

        foreach (var d in _snapshot.PhysicalDisks)

        {

            d.SmartPredictFailure = null;

            d.SmartReason = "";

            d.SmartAttributes.Clear();

        }



        List<(string InstanceName, bool Active, bool PredictFailure, string Reason)> statuses = new();

        try

        {

            using var s = new ManagementObjectSearcher(@"root\wmi",

                "SELECT InstanceName, Active, PredictFailure, Reason FROM MSStorageDriver_FailurePredictStatus");

            foreach (ManagementObject obj in s.Get())

            {

                var inst = obj["InstanceName"]?.ToString() ?? "";

                var active = Convert.ToBoolean(obj["Active"] ?? false);

                var pred = Convert.ToBoolean(obj["PredictFailure"] ?? false);

                var reason = obj["Reason"]?.ToString() ?? "";

                statuses.Add((inst, active, pred, reason));

            }

        }

        catch

        {

            return;

        }



        Dictionary<string, byte[]?> vendorByInstance = new(StringComparer.OrdinalIgnoreCase);

        try

        {

            using var s = new ManagementObjectSearcher(@"root\wmi",

                "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData");

            foreach (ManagementObject obj in s.Get())

            {

                var inst = obj["InstanceName"]?.ToString() ?? "";

                var vs = obj["VendorSpecific"];

                vendorByInstance[inst] = ToByteArray(vs);

            }

        }

        catch

        {

            // optional

        }



        foreach (var disk in _snapshot.PhysicalDisks)

        {

            var match = FindBestSmartMatch(disk, statuses);

            if (match is null)

                continue;



            disk.SmartPredictFailure = match.Value.PredictFailure;

            disk.SmartReason = match.Value.Reason;



            if (vendorByInstance.TryGetValue(match.Value.InstanceName, out var raw) && raw is { Length: > 0 })

                TryParseAtaSmartVendorBlock(raw, disk.SmartAttributes);

        }

    }



    private static (string InstanceName, bool Active, bool PredictFailure, string Reason)? FindBestSmartMatch(

        PhysicalDiskInfo disk,

        List<(string InstanceName, bool Active, bool PredictFailure, string Reason)> statuses)

    {

        (string InstanceName, bool Active, bool PredictFailure, string Reason)? best = null;
        var bestScore = 0;

        var serial = disk.SerialNumber.Trim();

        var model = disk.Model;

        var pnp = disk.PnpDeviceId;



        foreach (var st in statuses)

        {

            if (!st.Active) continue;

            var inst = st.InstanceName;

            if (inst.Length == 0) continue;



            var score = 0;

            if (serial.Length >= 4 && inst.Contains(serial, StringComparison.OrdinalIgnoreCase))

                score += 10;

            if (model.Length >= 3)

            {

                var token = model.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

                if (token.Length >= 3 && inst.Contains(token, StringComparison.OrdinalIgnoreCase))

                    score += 5;

            }



            if (pnp.Length > 0)

            {

                var norm = pnp.Replace('\\', '#');

                if (inst.Contains(norm, StringComparison.OrdinalIgnoreCase))

                    score += 8;

            }



            var m = Regex.Match(disk.DeviceId, @"PHYSICALDRIVE(\d+)", RegexOptions.IgnoreCase);

            if (m.Success && inst.Contains(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase))

                score += 3;



            if (score == 0) continue;

            if (score > bestScore)
            {
                bestScore = score;
                best = st;
            }
        }

        return bestScore > 0 ? best : null;

    }



    private static byte[]? ToByteArray(object? vs)

    {

        switch (vs)

        {

            case byte[] b:

                return b;

            case int[] ints:

                var buf = new byte[ints.Length];

                for (var i = 0; i < ints.Length; i++)

                {

                    var v = ints[i];

                    buf[i] = (byte)(v & 0xFF);

                }



                return buf;

            default:

                return null;

        }

    }



    private static void TryParseAtaSmartVendorBlock(byte[] raw, List<SmartAttributeInfo> target)

    {

        if (raw.Length < 14) return;



        var off = 2;

        while (off + 12 <= raw.Length)

        {

            var id = raw[off];

            if (id is 0 or 0xFE) break;



            var flags = BitConverter.ToUInt16(raw, off + 1);

            var current = raw[off + 3];

            var worst = raw[off + 4];

            var threshold = raw[off + 5];

            ulong raw48 = raw[off + 6]

                          | ((ulong)raw[off + 7] << 8)

                          | ((ulong)raw[off + 8] << 16)

                          | ((ulong)raw[off + 9] << 24)

                          | ((ulong)raw[off + 10] << 32)

                          | ((ulong)raw[off + 11] << 40);



            target.Add(new SmartAttributeInfo

            {

                Id = id,

                Name = SmartAttributeName(id),

                Flags = flags,

                Current = current,

                Worst = worst,

                Threshold = threshold,

                RawValue = raw48

            });



            off += 12;

        }

    }



    private static string SmartAttributeName(int id) => id switch

    {

        1 => "Raw_Read_Error_Rate",

        5 => "Reallocated_Sector_Ct",

        9 => "Power_On_Hours",

        12 => "Power_Cycle_Count",

        177 => "Wear_Leveling_Count",

        194 => "Temperature_Celsius",

        197 => "Current_Pending_Sector",

        198 => "Offline_Uncorrectable",

        199 => "UDMA_CRC_Error_Count",

        _ => $"Attr_{id}"

    };



    private void UpdateLogicalDisksFromWmi()

    {

        _snapshot.LogicalDisks.Clear();

        try

        {

            using var s = new ManagementObjectSearcher(

                "SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");

            foreach (ManagementObject obj in s.Get())

            {

                _snapshot.LogicalDisks.Add(new LogicalDiskInfo

                {

                    DeviceId = obj["DeviceID"]?.ToString() ?? "",

                    VolumeName = obj["VolumeName"]?.ToString() ?? "",

                    FileSystem = obj["FileSystem"]?.ToString() ?? "",

                    SizeBytes = Convert.ToInt64(obj["Size"] ?? 0L),

                    FreeBytes = Convert.ToInt64(obj["FreeSpace"] ?? 0L)

                });

            }

        }

        catch

        {

            // ignore

        }

    }



    private void UpdateNetworkAdaptersFromWmi()

    {

        _snapshot.NetworkAdapters.Clear();

        try

        {

            using var s = new ManagementObjectSearcher(

                @"SELECT Name, Manufacturer, MACAddress, NetEnabled, NetConnectionID, Speed, AdapterType, PhysicalAdapter

                  FROM Win32_NetworkAdapter

                  WHERE MACAddress IS NOT NULL AND MACAddress <> ''");

            foreach (ManagementObject obj in s.Get())

            {

                var physical = Convert.ToBoolean(obj["PhysicalAdapter"] ?? false);

                if (!physical) continue;



                _snapshot.NetworkAdapters.Add(new NetworkAdapterInfo

                {

                    Name = obj["Name"]?.ToString() ?? "",

                    Manufacturer = obj["Manufacturer"]?.ToString() ?? "",

                    MacAddress = obj["MACAddress"]?.ToString() ?? "",

                    NetEnabled = Convert.ToBoolean(obj["NetEnabled"] ?? false),

                    NetConnectionId = obj["NetConnectionID"]?.ToString() ?? "",

                    SpeedBitsPerSec = Convert.ToUInt64(obj["Speed"] ?? 0u),

                    AdapterType = obj["AdapterType"]?.ToString() ?? ""

                });

            }

        }

        catch

        {

            // ignore

        }

    }



    private void UpdateCpuAndGpuSensors()

    {

        _snapshot.CpuSensors.Clear();

        _snapshot.AllHardwareSensors.Clear();

        double? cpuTemp = null;

        double? cpuLoad = null;



        if (_computer is null)

            return;



        foreach (var hw in _computer.Hardware)
            ReadHardwareRecursive(hw, ref cpuTemp, ref cpuLoad);



        if (cpuTemp is null)

            cpuTemp = TryReadThermalZoneCelsius();



        _snapshot.CpuTemperatureC = cpuTemp;

        _snapshot.CpuLoadPercent = cpuLoad;

        HardwareSensorCategorizer.Apply(_snapshot);

    }

    private void ReadHardwareRecursive(IHardware hw, ref double? cpuTemp, ref double? cpuLoad)
    {
        hw.Update();
        foreach (var s in hw.Sensors)
        {
            if (!s.Value.HasValue)
                continue;

            TryAppendAllHardwareSensor(hw, s);
            TryAppendCpuSensor(hw, s, ref cpuTemp, ref cpuLoad);
        }

        foreach (var sub in hw.SubHardware)
            ReadHardwareRecursive(sub, ref cpuTemp, ref cpuLoad);
    }

    private void TryAppendCpuSensor(IHardware hw, ISensor s, ref double? cpuTemp, ref double? cpuLoad)
    {
        if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature)
        {
            var v = s.Value!.Value;
            if (v <= -50 || v >= 150)
                return;

            _snapshot.CpuSensors.Add(new SensorReading
            {
                Name = s.Name,
                Kind = SensorKind.Temperature,
                Unit = "°C",
                Value = v,
                TimestampLocal = DateTime.Now
            });

            if (hw.HardwareType == HardwareType.Cpu &&
                (cpuTemp is null
                 || s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                 || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
                 || s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)))
            {
                cpuTemp = v;
            }
        }
        else if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Load)
        {
            var v = s.Value!.Value;
            if (v < 0 || v > 100)
                return;

            _snapshot.CpuSensors.Add(new SensorReading
            {
                Name = s.Name,
                Kind = SensorKind.Load,
                Unit = "%",
                Value = v,
                TimestampLocal = DateTime.Now
            });

            if (hw.HardwareType == HardwareType.Cpu &&
                (cpuLoad is null
                 || s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)
                 || s.Name.Contains("CPU Total", StringComparison.OrdinalIgnoreCase)))
            {
                cpuLoad = v;
            }
        }
    }



    private void TryAppendAllHardwareSensor(IHardware hw, ISensor s)

    {

        var v = s.Value!.Value;

        var st = s.SensorType;

        if (st == SensorType.Fan && (v < 0 || v > 200_000))

            return;

        if (st == SensorType.Temperature && (v < -80 || v > 220))

            return;

        var (kind, unit) = MapLibreSensorKind(st);

        _snapshot.AllHardwareSensors.Add(new SensorReading

        {

            Name = $"{hw.Name} / {s.Name}",

            HardwareGroup = hw.HardwareType.ToString(),

            Kind = kind,

            Unit = unit,

            Value = v,

            TimestampLocal = DateTime.Now

        });

    }



    private static (SensorKind Kind, string Unit) MapLibreSensorKind(SensorType t)

    {

        return t switch

        {

            SensorType.Temperature => (SensorKind.Temperature, "°C"),

            SensorType.Load => (SensorKind.Load, "%"),

            SensorType.Voltage => (SensorKind.Voltage, "V"),

            SensorType.Clock => (SensorKind.Clock, "MHz"),

            SensorType.Fan => (SensorKind.Fan, "RPM"),

            SensorType.Power => (SensorKind.Power, "W"),

            SensorType.Current => (SensorKind.Other, "A"),

            SensorType.Energy => (SensorKind.Other, "J"),

            SensorType.Data => (SensorKind.Other, ""),

            SensorType.SmallData => (SensorKind.Other, ""),

            SensorType.Throughput => (SensorKind.Other, ""),

            SensorType.Level => (SensorKind.Other, "%"),

            SensorType.Factor => (SensorKind.Other, ""),

            SensorType.Frequency => (SensorKind.Clock, "Hz"),

            _ => (SensorKind.Other, "")

        };

    }



    private void UpdateDynamicMemoryFromWmi()

    {

        using var osSearcher = new ManagementObjectSearcher(

            "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");

        foreach (ManagementObject obj in osSearcher.Get())

        {

            var totalKb = Convert.ToInt64(obj["TotalVisibleMemorySize"] ?? 0L);

            var freeKb = Convert.ToInt64(obj["FreePhysicalMemory"] ?? 0L);

            _snapshot.TotalRamBytes = totalKb * 1024;

            _snapshot.FreeRamBytes = freeKb * 1024;

            break;

        }

    }



    private static double? TryReadThermalZoneCelsius()

    {

        try

        {

            using var searcher = new ManagementObjectSearcher(@"root\WMI",

                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            foreach (ManagementObject obj in searcher.Get())

            {

                var raw = Convert.ToDouble(obj["CurrentTemperature"] ?? 0d);

                var c = raw / 10.0 - 273.15;

                if (c > -50 && c < 150)

                    return c;

            }

        }

        catch

        {

            // ignore

        }



        return null;

    }



    private static string DecodeMemoryType(int smbType) => smbType switch

    {

        20 => "DDR",

        21 => "DDR2",

        24 => "DDR3",

        26 => "DDR4",

        34 => "DDR5",

        _ => smbType == 0 ? "Unknown" : $"Type {smbType}"

    };



    private static string DecodeFormFactor(uint code) => code switch

    {

        0 => "",

        1 => "Other",

        2 => "SIP",

        3 => "DIP",

        4 => "ZIP",

        5 => "SOJ",

        6 => "Proprietary",

        7 => "SIMM",

        8 => "DIMM",

        9 => "TSOP",

        10 => "PGA",

        11 => "RIMM",

        12 => "SODIMM",

        13 => "SRIMM",

        14 => "SMD",

        15 => "QFP",

        16 => "TQFP",

        17 => "SOIC",

        18 => "LCC",

        19 => "PLCC",

        20 => "BGA",

        21 => "FPBGA",

        22 => "LGA",

        _ => $"FF {code}"

    };



    private static string DecodeTypeDetail(uint bits)

    {

        if (bits == 0) return "";

        var parts = new List<string>();

        void test(uint mask, string label)

        {

            if ((bits & mask) != 0) parts.Add(label);

        }



        test(1, "Reserved");

        test(2, "Other");

        test(4, "Unknown");

        test(8, "Fast-paged");

        test(16, "Static column");

        test(32, "Pseudo-static");

        test(64, "RAMBUS");

        test(128, "Synchronous");

        test(256, "CMOS");

        test(512, "EDO");

        test(1024, "Window DRAM");

        test(2048, "Cache DRAM");

        test(4096, "Non-volatile");

        test(8192, "Registered");

        test(16384, "Unbuffered");

        test(32768, "LRDIMM");

        return parts.Count == 0 ? $"0x{bits:X}" : string.Join(", ", parts);

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

            CpuSensors = s.CpuSensors.Select(x => new SensorReading

            {

                Name = x.Name,

                HardwareGroup = x.HardwareGroup,

                Kind = x.Kind,

                Unit = x.Unit,

                Value = x.Value,

                TimestampLocal = x.TimestampLocal

            }).ToList(),

            AllHardwareSensors = s.AllHardwareSensors.Select(x => new SensorReading

            {

                Name = x.Name,

                HardwareGroup = x.HardwareGroup,

                Kind = x.Kind,

                Unit = x.Unit,

                Value = x.Value,

                TimestampLocal = x.TimestampLocal

            }).ToList(),

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

                BankLabel = x.BankLabel,

                DeviceLocator = x.DeviceLocator,

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

                Model = d.Model,

                SerialNumber = d.SerialNumber,

                InterfaceType = d.InterfaceType,

                MediaType = d.MediaType,

                SizeBytes = d.SizeBytes,

                Partitions = d.Partitions,

                SmartPredictFailure = d.SmartPredictFailure,

                SmartReason = d.SmartReason,

                PnpDeviceId = d.PnpDeviceId,

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

            CpuPerCoreClocks = s.CpuPerCoreClocks.Select(x => new SensorReading

            {

                Name = x.Name,

                HardwareGroup = x.HardwareGroup,

                Kind = x.Kind,

                Unit = x.Unit,

                Value = x.Value,

                TimestampLocal = x.TimestampLocal

            }).ToList(),

            CpuPerCoreVoltages = s.CpuPerCoreVoltages.Select(x => new SensorReading

            {

                Name = x.Name,

                HardwareGroup = x.HardwareGroup,

                Kind = x.Kind,

                Unit = x.Unit,

                Value = x.Value,

                TimestampLocal = x.TimestampLocal

            }).ToList(),

            VrmChipsetSensors = s.VrmChipsetSensors.Select(x => new SensorReading

            {

                Name = x.Name,

                HardwareGroup = x.HardwareGroup,

                Kind = x.Kind,

                Unit = x.Unit,

                Value = x.Value,

                TimestampLocal = x.TimestampLocal

            }).ToList(),

            FanSensors = s.FanSensors.Select(x => new SensorReading

            {

                Name = x.Name,

                HardwareGroup = x.HardwareGroup,

                Kind = x.Kind,

                Unit = x.Unit,

                Value = x.Value,

                TimestampLocal = x.TimestampLocal

            }).ToList(),

            StorageTemperatureSensors = s.StorageTemperatureSensors.Select(x => new SensorReading

            {

                Name = x.Name,

                HardwareGroup = x.HardwareGroup,

                Kind = x.Kind,

                Unit = x.Unit,

                Value = x.Value,

                TimestampLocal = x.TimestampLocal

            }).ToList(),

            RamTimingSummaryLines = s.RamTimingSummaryLines.ToList()

        };

    }



    public void Dispose()

    {

        _computer?.Close();

        _computer = null;

    }

}


