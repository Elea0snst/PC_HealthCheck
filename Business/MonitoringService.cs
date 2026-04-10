using PC_HealthCheck.Core;
using PC_HealthCheck.DAL;

namespace PC_HealthCheck.Business;

public sealed class MonitoringService : IDisposable
{
    private readonly IHardwareProvider _provider;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    public event EventHandler<DeviceSnapshot>? SnapshotUpdated;

    public bool IsRunning => _cts is not null;
    public int UpdateCount { get; private set; }
    public DateTime StartedLocal { get; private set; } = DateTime.Now;
    public DateTime LastUpdateLocal { get; private set; } = DateTime.Now;

    public MonitoringService(IHardwareProvider provider)
    {
        _provider = provider;
    }

    public async Task<bool> StartAsync(TimeSpan interval)
    {
        Stop();

        if (!_provider.IsInitialized)
        {
            var ok = await _provider.InitializeAsync();
            if (!ok)
                return false;
        }

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(interval);
        UpdateCount = 0;
        StartedLocal = DateTime.Now;
        LastUpdateLocal = DateTime.Now;

        _ = Task.Run(async () =>
        {
            try
            {
                while (_timer is not null && await _timer.WaitForNextTickAsync(_cts.Token))
                {
                    var snap = await _provider.ReadSnapshotAsync();
                    UpdateCount++;
                    LastUpdateLocal = DateTime.Now;
                    SnapshotUpdated?.Invoke(this, snap);
                }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        });

        return true;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}

