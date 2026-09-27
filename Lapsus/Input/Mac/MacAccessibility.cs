using System.Diagnostics;
using System.Runtime.Versioning;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal static class MacAccessibility
{
    public static bool IsProcessTrusted()
    {
        return MacOSNativeMethods.AXIsProcessTrusted();
    }

    // Shows the Accessibility trust prompt if not granted yet.
    public static void PromptIfNeeded()
    {
        if (IsProcessTrusted())
            return;

        MacOSNativeMethods.AXIsProcessTrustedWithOptions(MacOSNativeMethods.GetAccessibilityPromptOptions());
    }

    public static void OpenSettings()
    {
        OpenPrivacyPane("Privacy_Accessibility");
    }

    public static bool IsInputMonitoringGranted()
    {
        return MacOSNativeMethods.IOHIDCheckAccess(MacOSNativeMethods.HidRequestTypeListenEvent)
               == MacOSNativeMethods.HidAccessTypeGranted;
    }

    // Shows the Input Monitoring prompt once; later calls return the cached answer.
    public static void RequestInputMonitoringIfNeeded()
    {
        if (IsInputMonitoringGranted())
            return;

        MacOSNativeMethods.IOHIDRequestAccess(MacOSNativeMethods.HidRequestTypeListenEvent);
    }

    public static void OpenInputMonitoringSettings()
    {
        OpenPrivacyPane("Privacy_ListenEvent");
    }

    private static void OpenPrivacyPane(string pane)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"x-apple.systempreferences:com.apple.preference.security?{pane}",
            UseShellExecute = false
        });
    }
}
