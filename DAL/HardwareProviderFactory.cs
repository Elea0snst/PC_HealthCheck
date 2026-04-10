using System.Runtime.InteropServices;

namespace PC_HealthCheck.DAL;

public static class HardwareProviderFactory
{
    public static IHardwareProvider CreateDefault()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsHardwareProvider();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxHardwareProvider();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new MacHardwareProvider();

        return new UnixHardwareProvider();
    }
}
