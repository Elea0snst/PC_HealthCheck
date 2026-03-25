namespace PC_HealthCheck.Core;

/// <summary>
/// Decodes Intel/AMD-style CPU signature from Win32_Processor.ProcessorId (low dword ≈ CPUID eax leaf 1).
/// </summary>
public static class CpuSignatureDecoder
{
    public static (uint Family, uint Model, uint Stepping, string RawSource) FromProcessorIdHex(string? processorIdHex)
    {
        if (string.IsNullOrWhiteSpace(processorIdHex))
            return (0, 0, 0, "");

        var hex = processorIdHex.Trim();
        var low = hex.Length >= 8 ? hex[^8..] : hex;
        if (!uint.TryParse(low, System.Globalization.NumberStyles.HexNumber, null, out var eax))
            return (0, 0, 0, hex);

        var (family, model, stepping) = DecodeFamilyModelStepping(eax);
        return (family, model, stepping, hex);
    }

    public static (uint Family, uint Model, uint Stepping) DecodeFamilyModelStepping(uint eaxLeaf1)
    {
        var stepping = eaxLeaf1 & 0xFu;
        var baseModel = (eaxLeaf1 >> 4) & 0xFu;
        var baseFamily = (eaxLeaf1 >> 8) & 0xFu;
        var extModel = (eaxLeaf1 >> 16) & 0xFu;
        var extFamily = (eaxLeaf1 >> 20) & 0xFFu;

        uint family = baseFamily;
        uint model = baseModel;
        if (baseFamily == 0xFu)
        {
            family += extFamily;
            model += extModel << 4;
        }
        else if (baseFamily == 6u)
        {
            model += extModel << 4;
        }

        return (family, model, stepping);
    }

    public static string DescribeMicroarchitecture(string? manufacturer, uint family, uint model)
    {
        var m = manufacturer ?? "";
        var intel = m.Contains("Intel", StringComparison.OrdinalIgnoreCase);
        var amd = m.Contains("AMD", StringComparison.OrdinalIgnoreCase) || m.Contains("AuthenticAMD", StringComparison.OrdinalIgnoreCase);

        if (intel && family == 6)
            return DescribeIntelFamily6(model);
        if (amd && (family == 0x17 || family == 0x19 || family == 0x1A))
            return DescribeAmdZenFamily(family, model);

        if (family == 0 && model == 0)
            return "";
        return intel ? $"Intel family {family}, model {model}" : amd ? $"AMD family {family}, model {model}" : $"Family {family}, model {model}";
    }

    private static string DescribeAmdZenFamily(uint family, uint model)
    {
        if (family == 0x17)
        {
            return model switch
            {
                <= 0x1F => "Zen / Zen+ (Summit/Pinnacle)",
                _ => "Zen (Family 17h)"
            };
        }

        if (family == 0x19)
        {
            return model switch
            {
                <= 0x0F => "Zen 3",
                <= 0x2F => "Zen 3+ / Zen 4",
                _ => "Zen 4 / Zen 4c"
            };
        }

        if (family == 0x1A)
            return "Zen 5 (Family 1Ah)";

        return $"AMD family {family}h, model {model}";
    }

    private static string DescribeIntelFamily6(uint model)
    {
        return model switch
        {
            >= 0xB7 and <= 0xBF => "Raptor Lake / Raptor Lake Refresh",
            >= 0x97 and <= 0x9A => "Alder Lake",
            >= 0x8F and <= 0x96 => "Rocket Lake / Tiger Lake (select)",
            >= 0x7D and <= 0x7F => "Ice Lake",
            >= 0x66 and <= 0x6C => "Comet Lake",
            >= 0x5C and <= 0x5F => "Kaby Lake / Coffee Lake",
            0x4E or 0x5E => "Skylake",
            >= 0x3C and <= 0x3F => "Haswell",
            0x2A or 0x2D => "Sandy Bridge",
            0x25 or 0x2C or 0x2F => "Westmere",
            0x1A or 0x1E or 0x1F or 0x2E => "Nehalem",
            _ => $"Core microarch (model {model})"
        };
    }
}
