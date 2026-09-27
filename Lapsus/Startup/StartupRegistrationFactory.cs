using System;

namespace Lapsus.Startup;

internal static class StartupRegistrationFactory
{
    public static IStartupRegistration CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return PackageIdentity.IsPackaged
                ? new WindowsStoreStartupRegistration()
                : new WindowsStartupRegistration();
        if (OperatingSystem.IsMacOS())
            return new MacOsStartupRegistration();
        if (OperatingSystem.IsLinux())
            return new LinuxStartupRegistration();

        return new NullStartupRegistration();
    }
}
