using PC_HealthCheck.Core;

namespace PC_HealthCheck.Business;

/// <summary>Условные эталонные значения для сравнения (та же метрика, что и у быстрого бенчмарка в приложении).</summary>
internal static class ReferenceScoreTable
{
    private static readonly (string Key, double CpuMegaOpsPerSec, double RamGbps)[] Rows =
    {
        ("Core Ultra 9 285K", 520, 70),
        ("Core i9-14900K", 510, 68),
        ("Core i9-13900K", 480, 65),
        ("Core i9-12900K", 450, 62),
        ("Ryzen 9 9950X", 540, 74),
        ("Ryzen 9 7950X", 500, 72),
        ("Ryzen 9 7900X", 420, 70),
        ("Ryzen 7 9800X3D", 400, 70),
        ("Ryzen 7 7800X3D", 380, 68),
        ("Ryzen 7 7700X", 360, 65),
        ("Ryzen 5 7600X", 340, 64),
        ("Ryzen 5 5600X", 280, 48),
        ("Ryzen 5 3600", 220, 42),
        ("Core i7-14700", 400, 55),
        ("Core i7-13700", 380, 54),
        ("Core i5-14600", 320, 52),
        ("Core i5-13600K", 310, 52),
        ("Core i5-13400", 260, 48),
        ("Core i5-12400", 240, 45),
        ("Core i5-11400", 200, 40),
        ("Core i3-12100", 180, 42),
        ("Core i5-8400", 150, 38),
        ("Core i5-7400", 120, 30),
        ("Xeon W", 320, 55),
        ("Xeon E", 200, 45),
        ("Celeron", 60, 22),
        ("Pentium Gold", 90, 28),
        ("Pentium", 80, 25),
    };

    public static (double? CpuMegaOps, double? RamGbps, string MatchedLabel) Match(DeviceSnapshot? s)
    {
        var cpuName = s?.CpuName ?? "";
        foreach (var row in Rows.OrderByDescending(r => r.Key.Length))
        {
            if (cpuName.Contains(row.Key, StringComparison.OrdinalIgnoreCase))
            {
                var ram = AdjustRamRef(row.RamGbps, s?.RamType ?? "");
                return (row.CpuMegaOpsPerSec, ram, row.Key);
            }
        }

        return (null, null, "");
    }

    private static double AdjustRamRef(double baseline, string ramType)
    {
        if (ramType.Contains("DDR5", StringComparison.OrdinalIgnoreCase))
            return Math.Max(baseline, 56);
        if (ramType.Contains("DDR4", StringComparison.OrdinalIgnoreCase))
            return Math.Min(baseline, 52);
        if (ramType.Contains("DDR3", StringComparison.OrdinalIgnoreCase))
            return Math.Min(baseline, 28);
        return baseline;
    }
}
