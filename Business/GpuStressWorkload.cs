using System.Numerics;
using System.Runtime.InteropServices;

namespace PC_HealthCheck.Business;

/// <summary>Нагрузка GPU (D3D11 compute на Windows) или SIMD fallback на CPU.</summary>
public static class GpuStressWorkload
{
    public static Task<(string Mode, bool UsedDiscreteGpu)> RunAsync(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                return Task.FromResult(RunWindowsD3D11(duration, cancellationToken));
            }
            catch
            {
                // fallback below
            }
        }

        return Task.FromResult(RunSimdFallback(duration, cancellationToken));
    }

    private static (string Mode, bool UsedDiscreteGpu) RunSimdFallback(TimeSpan duration, CancellationToken ct)
    {
        var workers = Math.Max(1, Environment.ProcessorCount);
        var end = DateTime.UtcNow + duration;
        var tasks = new Task[workers];
        for (var i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                var data = new float[1 << 20];
                for (var k = 0; k < data.Length; k++)
                    data[k] = k * 0.0001f;

                var width = Vector<float>.Count;
                while (!ct.IsCancellationRequested && DateTime.UtcNow < end)
                {
                    if (Vector.IsHardwareAccelerated)
                    {
                        var i = 0;
                        for (; i <= data.Length - width; i += width)
                        {
                            var v = new Vector<float>(data, i);
                            v = Vector.SquareRoot(Vector.Abs(v) + Vector<float>.One);
                            v = Vector.Sin(v) * Vector.Cos(v) + v;
                            v.CopyTo(data, i);
                        }

                        for (; i < data.Length; i++)
                            data[i] = MathF.Sin(data[i]) * MathF.Cos(data[i]) + MathF.Sqrt(MathF.Abs(data[i]) + 1f);
                    }
                    else
                    {
                        for (var j = 0; j < data.Length; j++)
                            data[j] = MathF.Sin(data[j]) * MathF.Cos(data[j]) + MathF.Sqrt(MathF.Abs(data[j]) + 1f);
                    }
                }
            }, ct);
        }

        Task.WaitAll(tasks);
        return ("SIMD CPU (GPU API недоступен)", false);
    }

    private static (string Mode, bool UsedDiscreteGpu) RunWindowsD3D11(TimeSpan duration, CancellationToken ct)
    {
        return WindowsD3D11GpuStress.Run(duration, ct);
    }
}
