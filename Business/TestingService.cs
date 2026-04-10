using PC_HealthCheck.Core;

namespace PC_HealthCheck.Business;

public sealed class TestingService : IDisposable
{
    private CancellationTokenSource? _cts;

    public event EventHandler<double>? ProgressChanged;
    public event EventHandler<StressResult>? Completed;

    public bool IsRunning => _cts is not null;

    public Task<bool> StartCpuTestAsync(int seconds)
    {
        return StartAsync("CPU Stress", seconds, async (end, token) =>
        {
            var workers = Math.Max(1, Environment.ProcessorCount);
            var tasks = new List<Task>(workers);
            for (int i = 0; i < workers; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    double x = 1.0001;
                    while (!token.IsCancellationRequested && DateTime.UtcNow < end)
                        x = Math.Sqrt(x * 1.0000001) + Math.Sin(x);
                }, token));
            }
            await TrackProgressAsync(seconds, end, token, 300);
            token.ThrowIfCancellationRequested();
            await Task.WhenAll(tasks);
        });
    }

    public Task<bool> StartRamTestAsync(int seconds)
    {
        return StartAsync("RAM Stress", seconds, async (end, token) =>
        {
            var size = 128 * 1024 * 1024;
            var buf = new byte[size];
            var rnd = new Random();
            while (!token.IsCancellationRequested && DateTime.UtcNow < end)
            {
                var pattern = (byte)rnd.Next(0, 255);
                for (int i = 0; i < buf.Length; i += 4096)
                    buf[i] = (byte)(pattern ^ (i % 251));
                for (int i = 0; i < buf.Length; i += 4096)
                {
                    var expected = (byte)(pattern ^ (i % 251));
                    if (buf[i] != expected)
                        throw new Exception("RAM pattern mismatch");
                }
                await TrackProgressAsync(seconds, end, token, 250);
            }
            token.ThrowIfCancellationRequested();
        });
    }

    public Task<bool> StartGpuTestAsync(int seconds)
    {
        return StartAsync("GPU Stress (compute)", seconds, async (end, token) =>
        {
            // Cross-platform compute-heavy fallback without native GPU APIs.
            var workers = Math.Max(1, Environment.ProcessorCount / 2);
            var tasks = new List<Task>(workers);
            for (int i = 0; i < workers; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    var data = new float[1 << 20];
                    for (int k = 0; k < data.Length; k++) data[k] = k * 0.0001f;
                    while (!token.IsCancellationRequested && DateTime.UtcNow < end)
                    {
                        for (int j = 0; j < data.Length; j++)
                            data[j] = MathF.Sin(data[j]) * MathF.Cos(data[j]) + MathF.Sqrt(MathF.Abs(data[j]) + 1f);
                    }
                }, token));
            }
            await TrackProgressAsync(seconds, end, token, 250);
            token.ThrowIfCancellationRequested();
            await Task.WhenAll(tasks);
        });
    }

    public Task<bool> StartDiskTestAsync(int seconds)
    {
        return StartAsync("Disk Stress", seconds, async (end, token) =>
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"pc_healthcheck_disk_{Guid.NewGuid():N}.bin");
            var block = new byte[4 * 1024 * 1024];
            var rnd = Random.Shared;
            rnd.NextBytes(block);
            try
            {
                await using var fs = new FileStream(
                    tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, block.Length,
                    FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

                while (!token.IsCancellationRequested && DateTime.UtcNow < end)
                {
                    await fs.WriteAsync(block, token);
                    if (fs.Length > 512L * 1024 * 1024)
                    {
                        fs.Position = 0;
                        await fs.FlushAsync(token);
                        await fs.ReadExactlyAsync(block.AsMemory(0, Math.Min(block.Length, 1024 * 1024)), token);
                        fs.SetLength(0);
                    }
                    await TrackProgressAsync(seconds, end, token, 200);
                }
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        });
    }

    private async Task<bool> StartAsync(string testName, int seconds, Func<DateTime, CancellationToken, Task> body)
    {
        if (IsRunning) return false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var result = new StressResult
        {
            TestName = testName,
            Status = TestStatus.Running,
            StartedUtc = DateTime.UtcNow
        };
        _ = Task.Run(async () =>
        {
            try
            {
                var end = DateTime.UtcNow.AddSeconds(Math.Max(1, seconds));
                await body(end, token);
                result.Status = TestStatus.Completed;
            }
            catch (OperationCanceledException)
            {
                result.Status = TestStatus.Cancelled;
            }
            catch (Exception ex)
            {
                result.Status = TestStatus.Failed;
                result.Error = ex.Message;
            }
            finally
            {
                result.EndedUtc = DateTime.UtcNow;
                _cts?.Dispose();
                _cts = null;
                Completed?.Invoke(this, result);
            }
        });
        return true;
    }

    private async Task TrackProgressAsync(int seconds, DateTime end, CancellationToken token, int delayMs)
    {
        var p = 100.0 * (seconds - (end - DateTime.UtcNow).TotalSeconds) / Math.Max(1, seconds);
        ProgressChanged?.Invoke(this, Math.Clamp(p, 0, 100));
        await Task.Delay(delayMs, token);
    }

    public void Stop() => _cts?.Cancel();

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}

