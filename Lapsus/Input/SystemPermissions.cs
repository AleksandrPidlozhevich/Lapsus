using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Lapsus.Input;

// Mac TCC checks for settings; Windows reports granted (no prompt).
internal static class SystemPermissions
{
    public static bool AccessibilityGranted =>
        !OperatingSystem.IsMacOS() || MacAccessibility.IsProcessTrusted();

    public static bool InputMonitoringGranted =>
        !OperatingSystem.IsMacOS() || MacAccessibility.IsInputMonitoringGranted();

    // Asking first is what puts Lapsus into the list, so the pane opens with it already there and the
    // user only switches it on. A Mac that lost the entry (the app was removed, or a build signed
    // differently replaced it) would otherwise show a list without Lapsus in it. The request can wait
    // on the system, so it stays off the UI thread.
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

    // Shows macOS's own "Lapsus would like to..." dialogs the first time each is needed, instead of
    // leaving the user to find the app in System Settings on their own. A no-op once already decided.
    public static void PromptForPermissionsIfNeeded()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        MacAccessibility.PromptIfNeeded();
        MacAccessibility.RequestInputMonitoringIfNeeded();
    }
}
