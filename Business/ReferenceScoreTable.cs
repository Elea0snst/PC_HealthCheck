using PC_HealthCheck.Core;

namespace PC_HealthCheck.Business;

/// <summary>Эталоны для сравнения (метрики того же набора тестов, что и BenchmarkService).</summary>
internal static class ReferenceScoreTable
{
    private static readonly (string Key, double StMegaOps, double RamReadGbps, double RamWriteGbps, double SimdGflops)[] Rows =
    {
        ("Core Ultra 9 285K", 520, 58, 52, 42),
        ("Core i9-14900K", 510, 56, 50, 40),
        ("Core i9-13900K", 480, 54, 48, 38),
        ("Core i9-12900K", 450, 50, 44, 35),
        ("Ryzen 9 9950X", 540, 60, 54, 44),
        ("Ryzen 9 7950X", 500, 58, 52, 42),
        ("Ryzen 9 7900X", 420, 54, 48, 36),
        ("Ryzen 7 9800X3D", 400, 56, 50, 34),
        ("Ryzen 7 7800X3D", 380, 54, 48, 32),
        ("Ryzen 7 7700X", 360, 52, 46, 30),
        ("Ryzen 5 7600X", 340, 50, 44, 28),
        ("Ryzen 5 5600X", 280, 38, 34, 22),
        ("Ryzen 5 3600", 220, 32, 28, 18),
        ("Core i7-14700", 400, 48, 42, 30),
        ("Core i7-13700", 380, 46, 40, 28),
        ("Core i5-14600", 320, 44, 38, 26),
        ("Core i5-13600K", 310, 44, 38, 26),
        ("Core i5-13400", 260, 40, 36, 22),
        ("Core i5-12400", 240, 38, 34, 20),
        ("Core i5-11400", 200, 34, 30, 17),
        ("Core i3-12100", 180, 36, 32, 16),
        ("Core i5-8400", 150, 30, 26, 14),
        ("Core i5-7400", 120, 26, 22, 11),
        ("Xeon W", 320, 48, 42, 28),
        ("Xeon E", 200, 36, 32, 18),
        ("Celeron", 60, 18, 16, 6),
        ("Pentium Gold", 90, 22, 20, 8),
        ("Pentium", 80, 20, 18, 7),
    };

    public sealed record ReferenceMatch(
        double? SingleThreadMegaOps,
        double? MultiThreadMegaOps,
        double? RamReadGbps,
        double? RamWriteGbps,
        double? RamCombinedGbps,
        double? SimdGflops,
        double? CompositeIndex,
        string MatchedLabel);

    public static ReferenceMatch Match(DeviceSnapshot? s)
    {
        var cpuName = s?.CpuName ?? "";
        foreach (var row in Rows.OrderByDescending(r => r.Key.Length))
        {
            if (!cpuName.Contains(row.Key, StringComparison.OrdinalIgnoreCase))
                continue;

            var threads = Math.Max(1, s?.CpuThreads ?? Environment.ProcessorCount);
            var mtEfficiency = threads switch
            {
                <= 4 => 0.88,
                <= 8 => 0.82,
                <= 16 => 0.78,
                _ => 0.72
            };
            var mt = row.StMegaOps * threads * mtEfficiency;
            var ramRead = AdjustRamRef(row.RamReadGbps, s?.RamType ?? "");
            var ramWrite = AdjustRamRef(row.RamWriteGbps, s?.RamType ?? "");
            var composite = ComputeCompositeIndex(
                row.StMegaOps, mt, row.SimdGflops, ramRead, ramWrite,
                new ReferenceMatch(row.StMegaOps, mt, ramRead, ramWrite,
                    (ramRead + ramWrite) * 0.5, row.SimdGflops, 1000, row.Key));

            return new ReferenceMatch(
                row.StMegaOps,
                mt,
                ramRead,
                ramWrite,
                (ramRead + ramWrite) * 0.5,
                row.SimdGflops,
                composite,
                row.Key);
        }

        return new ReferenceMatch(null, null, null, null, null, null, null, "");
    }

    public static double ComputeCompositeIndex(
        double st, double mt, double simd, double ramRead, double ramWrite,
        ReferenceMatch refs)
    {
        if (refs.CompositeIndex is not double refComposite || refComposite <= 0)
            return 0;

        double Part(double value, double? reference) =>
            reference is > 0 ? value / reference.Value : 0;

        var score =
            0.35 * Part(st, refs.SingleThreadMegaOps) +
            0.35 * Part(mt, refs.MultiThreadMegaOps) +
            0.15 * Part(simd, refs.SimdGflops) +
            0.075 * Part(ramRead, refs.RamReadGbps) +
            0.075 * Part(ramWrite, refs.RamWriteGbps);

        return Math.Round(score * 1000, 0);
    }

    private static double AdjustRamRef(double baseline, string ramType)
    {
        if (ramType.Contains("DDR5", StringComparison.OrdinalIgnoreCase))
            return Math.Max(baseline, baseline * 1.05);
        if (ramType.Contains("DDR4", StringComparison.OrdinalIgnoreCase))
            return Math.Min(baseline, baseline * 0.92);
        if (ramType.Contains("DDR3", StringComparison.OrdinalIgnoreCase))
            return Math.Min(baseline, 22);
        return baseline;
    }
}
