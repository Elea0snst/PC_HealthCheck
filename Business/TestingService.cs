using PC_HealthCheck.Core;

namespace PC_HealthCheck.Business;

public sealed class TestingService : IDisposable
{
    private CancellationTokenSource? _cts;

    public event EventHandler<double>? ProgressChanged;
    public event EventHandler<StressResult>? Completed;

    public bool IsRunning => _cts is not null;

    public async Task<bool> StartCpuTestAsync(int seconds)
    {
        if (IsRunning) return false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var result = new StressResult
        {
            TestName = "CPU Stress",
            Status = TestStatus.Running,
            StartedUtc = DateTime.UtcNow
        };

        _ = Task.Run(async () =>
        {
            try
            {
                var end = DateTime.UtcNow.AddSeconds(seconds);
                var workers = Math.Max(1, Environment.ProcessorCount);

                var tasks = new List<Task>();
                for (int i = 0; i < workers; i++)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        double x = 1.0001;
                        while (!token.IsCancellationRequested && DateTime.UtcNow < end)
                            x = Math.Sqrt(x * 1.0000001) + Math.Sin(x);
                    }, token));
                }

                while (!token.IsCancellationRequested && DateTime.UtcNow < end)
                {
                    var p = 100.0 * (seconds - (end - DateTime.UtcNow).TotalSeconds) / seconds;
                    ProgressChanged?.Invoke(this, Math.Clamp(p, 0, 100));
                    await Task.Delay(300, token);
                }

                token.ThrowIfCancellationRequested();
                await Task.WhenAll(tasks);
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

    public async Task<bool> StartRamTestAsync(int seconds)
    {
        if (IsRunning) return false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var result = new StressResult
        {
            TestName = "RAM Stress",
            Status = TestStatus.Running,
            StartedUtc = DateTime.UtcNow
        };

        _ = Task.Run(async () =>
        {
            try
            {
                var end = DateTime.UtcNow.AddSeconds(seconds);
                var size = 128 * 1024 * 1024; // stable size
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

                    var p = 100.0 * (seconds - (end - DateTime.UtcNow).TotalSeconds) / seconds;
                    ProgressChanged?.Invoke(this, Math.Clamp(p, 0, 100));
                    await Task.Delay(250, token);
                }

                token.ThrowIfCancellationRequested();
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

    public void Stop() => _cts?.Cancel();

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}

