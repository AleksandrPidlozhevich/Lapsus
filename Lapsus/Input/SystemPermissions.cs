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
}
