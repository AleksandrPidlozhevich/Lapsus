using System;

namespace Lapsus.Input;

// Mac TCC checks for settings; Windows reports granted (no prompt).
internal static class SystemPermissions
{
    public static bool AccessibilityGranted =>
        !OperatingSystem.IsMacOS() || MacAccessibility.IsProcessTrusted();

    public static bool InputMonitoringGranted =>
        !OperatingSystem.IsMacOS() || MacAccessibility.IsInputMonitoringGranted();

    public static void OpenAccessibilitySettings()
    {
        if (OperatingSystem.IsMacOS())
            MacAccessibility.OpenSettings();
    }

    public static void OpenInputMonitoringSettings()
    {
        if (OperatingSystem.IsMacOS())
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
