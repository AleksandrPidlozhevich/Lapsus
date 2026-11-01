using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Lapsus.Input;

internal static class SystemPermissions
{
    public static bool AccessibilityGranted =>
        !OperatingSystem.IsMacOS() || MacAccessibility.IsProcessTrusted();

    public static bool InputMonitoringGranted =>
        !OperatingSystem.IsMacOS() || MacAccessibility.IsInputMonitoringGranted();

    // Asking first puts Lapsus into the list; a removed or re-signed app would otherwise be missing from it.
    // The request can wait on the system, so it stays off the UI thread.
    public static void OpenAccessibilitySettings()
    {
        if (OperatingSystem.IsMacOS())
            Task.Run(RequestAndOpenAccessibility);
    }

    public static void OpenInputMonitoringSettings()
    {
        if (OperatingSystem.IsMacOS())
            Task.Run(RequestAndOpenInputMonitoring);
    }

    [SupportedOSPlatform("macos")]
    private static void RequestAndOpenAccessibility()
    {
        MacAccessibility.PromptIfNeeded();
        MacAccessibility.OpenSettings();
    }

    [SupportedOSPlatform("macos")]
    private static void RequestAndOpenInputMonitoring()
    {
        MacAccessibility.RequestInputMonitoringIfNeeded();
        MacAccessibility.OpenInputMonitoringSettings();
    }

    public static void PromptForPermissionsIfNeeded()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        MacAccessibility.PromptIfNeeded();
        MacAccessibility.RequestInputMonitoringIfNeeded();
    }
}
