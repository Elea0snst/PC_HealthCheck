using PC_HealthCheck.Core;

namespace PC_HealthCheck.DAL;

public interface IHardwareProvider : IDisposable
{
    bool IsInitialized { get; }
    Task<bool> InitializeAsync();
    Task<DeviceSnapshot> ReadSnapshotAsync();
}
