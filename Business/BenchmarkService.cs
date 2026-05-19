using System.Diagnostics;
using System.Numerics;
using System.Threading;
using PC_HealthCheck.Core;

namespace PC_HealthCheck.Business;

public sealed class BenchmarkService
{
    private const int WarmupMs = 400;
    private const int CpuSingleMs = 2_000;
    private const int CpuMultiMs = 2_000;
    private const int CpuSimdMs = 1_500;
    private const int RamMs = 1_500;

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
        Warmup(ct);

        var cpuSt = MeasureCpuSingleThreadMegaOps(ct);
        var cpuMt = MeasureCpuMultiThreadMegaOps(ct);
        var cpuSimd = MeasureCpuSimdGflops(ct);
        var (ramRead, ramWrite) = MeasureRamBandwidthGbps(ct);
        var ramCombined = (ramRead + ramWrite) * 0.5;

        var refs = ReferenceScoreTable.Match(hint);
        var composite = ReferenceScoreTable.ComputeCompositeIndex(
            cpuSt, cpuMt, cpuSimd, ramRead, ramWrite, refs);

        return new BenchmarkRunResult
        {
            CpuFpMegaOpsPerSec = cpuSt,
            CpuSingleThreadMegaOps = cpuSt,
            CpuMultiThreadMegaOps = cpuMt,
            CpuSimdGflops = cpuSimd,
            RamBandwidthGbPerSec = ramCombined,
            RamReadBandwidthGbPerSec = ramRead,
            RamWriteBandwidthGbPerSec = ramWrite,
            CompositeIndex = composite,
            ReferenceCpuMegaOps = refs.SingleThreadMegaOps,
            ReferenceCpuMultiMegaOps = refs.MultiThreadMegaOps,
            ReferenceRamGbps = refs.RamCombinedGbps,
            ReferenceRamReadGbps = refs.RamReadGbps,
            ReferenceRamWriteGbps = refs.RamWriteGbps,
            ReferenceSimdGflops = refs.SimdGflops,
            ReferenceCompositeIndex = refs.CompositeIndex,
            ReferenceMatchLabel = refs.MatchedLabel
        };
    }

    private static void Warmup(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        double x = 1.0001;
        while (sw.ElapsedMilliseconds < WarmupMs && !ct.IsCancellationRequested)
        {
            for (var j = 0; j < 20_000; j++)
                x = Math.Sqrt(x * 1.0000001) + Math.Sin(x);
        }
    }

    private static double MeasureCpuSingleThreadMegaOps(CancellationToken ct)
    {
        const int inner = 50_000;
        long outer = 0;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < CpuSingleMs && !ct.IsCancellationRequested)
        {
            double x = 1.0001;
            for (var j = 0; j < inner; j++)
                x = Math.Sqrt(x * 1.0000001) + Math.Sin(x);
            outer++;
        }

        return OpsPerSecToMegaOps(outer * inner, sw.Elapsed);
    }

    private static double MeasureCpuMultiThreadMegaOps(CancellationToken ct)
    {
        const int inner = 12_000;
        var threads = Math.Max(1, Environment.ProcessorCount);
        var outer = 0L;
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < CpuMultiMs && !ct.IsCancellationRequested)
        {
            var localOuter = 0L;
            Parallel.For(0, threads, _ =>
            {
                var iters = 0L;
                double x = 1.0001 + Thread.CurrentThread.ManagedThreadId * 0.00001;
                for (var j = 0; j < inner; j++)
                    x = Math.Sqrt(x * 1.0000001) + Math.Sin(x);
                iters++;
                Interlocked.Add(ref localOuter, iters);
            });
            Interlocked.Add(ref outer, localOuter);
        }

        return OpsPerSecToMegaOps(outer * inner, sw.Elapsed);
    }

    private static double MeasureCpuSimdGflops(CancellationToken ct)
    {
        if (!Vector.IsHardwareAccelerated)
            return 0;

        const int vectorLen = 1024 * 256;
        var data = new float[vectorLen];
        for (var i = 0; i < data.Length; i++)
            data[i] = i * 0.0001f;

        var width = Vector<float>.Count;
        long flops = 0;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < CpuSimdMs && !ct.IsCancellationRequested)
        {
            var i = 0;
            for (; i <= data.Length - width; i += width)
            {
                var v = new Vector<float>(data, i);
                v = Vector.SquareRoot(Vector.Abs(v) + Vector<float>.One);
                v = Vector.Sin(v) * Vector.Cos(v) + v;
                v.CopyTo(data, i);
                flops += width * 6;
            }

            for (; i < data.Length; i++)
            {
                data[i] = MathF.Sin(data[i]) * MathF.Cos(data[i]) + MathF.Sqrt(MathF.Abs(data[i]) + 1f);
                flops += 6;
            }
        }

        var secs = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        return flops / secs / 1_000_000_000.0;
    }

    private static (double ReadGbps, double WriteGbps) MeasureRamBandwidthGbps(CancellationToken ct)
    {
        var threads = Math.Max(1, Environment.ProcessorCount);
        const int chunk = 8 * 1024 * 1024;
        var buffers = new byte[threads][];
        for (var t = 0; t < threads; t++)
            buffers[t] = new byte[chunk];

        long readBytes = 0;
        long writeBytes = 0;
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < RamMs && !ct.IsCancellationRequested)
        {
            Parallel.For(0, threads, t =>
            {
                var a = buffers[t];
                var b = new byte[chunk];
                Buffer.BlockCopy(a, 0, b, 0, chunk);
                Interlocked.Add(ref readBytes, chunk);
                Buffer.BlockCopy(b, 0, a, 0, chunk);
                Interlocked.Add(ref writeBytes, chunk);
            });
        }

        var secs = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        return (readBytes / secs / 1_000_000_000.0, writeBytes / secs / 1_000_000_000.0);
    }

    private static double OpsPerSecToMegaOps(long totalOps, TimeSpan elapsed) =>
        totalOps / Math.Max(elapsed.TotalSeconds, 0.001) / 1_000_000.0;
}
