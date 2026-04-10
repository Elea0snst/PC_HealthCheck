using System.Diagnostics;
using System.Threading;
using PC_HealthCheck.Core;

namespace PC_HealthCheck.Business;

public sealed class BenchmarkService
{
    private int _busy;

    public event EventHandler<BenchmarkRunResult>? Completed;

    public bool IsRunning => Volatile.Read(ref _busy) != 0;

    public async Task<bool> RunQuickSuiteAsync(DeviceSnapshot referenceHint, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
            return false;

        try
        {
            var result = await Task.Run(() => RunInternal(referenceHint, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            Completed?.Invoke(this, result);
            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static BenchmarkRunResult RunInternal(DeviceSnapshot hint, CancellationToken ct)
    {
        var cpu = MeasureCpuFpMegaOpsPerSec(ct);
        var ram = MeasureRamCopyGbps(ct);
        var (refCpu, refRam, label) = ReferenceScoreTable.Match(hint);
        return new BenchmarkRunResult
        {
            CpuFpMegaOpsPerSec = cpu,
            RamBandwidthGbPerSec = ram,
            ReferenceCpuMegaOps = refCpu,
            ReferenceRamGbps = refRam,
            ReferenceMatchLabel = label
        };
    }

    private static double MeasureCpuFpMegaOpsPerSec(CancellationToken ct)
    {
        const int inner = 50_000;
        long outer = 0;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 2_000 && !ct.IsCancellationRequested)
        {
            double x = 1.0001;
            for (var j = 0; j < inner; j++)
                x = Math.Sqrt(x * 1.0000001) + Math.Sin(x);
            outer++;
        }

        var secs = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        return outer * inner / 1_000_000.0 / secs;
    }

    private static double MeasureRamCopyGbps(CancellationToken ct)
    {
        const int sz = 64 * 1024 * 1024;
        var a = new byte[sz];
        var b = new byte[sz];
        long total = 0;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 1_500 && !ct.IsCancellationRequested)
        {
            Buffer.BlockCopy(a, 0, b, 0, sz);
            Buffer.BlockCopy(b, 0, a, 0, sz);
            total += sz * 2L;
        }

        var secs = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        return total / secs / 1_000_000_000.0;
    }
}
