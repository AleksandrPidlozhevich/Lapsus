using Lapsus.Startup;
using System.Runtime.Versioning;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal static class MacAccessibility
{
    // In the sandbox CGPreflightPostEventAccess and IOHIDCheckAccess keep their first answer for the life
    // of the process, while AXIsProcessTrusted follows the Accessibility switch as it is flipped; the event
    // tap can be created as soon as it is on, so capture starts without a restart.
    public static bool IsProcessTrusted()
    {
        return MacOSNativeMethods.AXIsProcessTrusted()
               || (MacSandbox.IsSandboxed && MacOSNativeMethods.CGPreflightPostEventAccess());
    }

    public static void PromptIfNeeded()
    {
        if (IsProcessTrusted())
            return;

        if (MacSandbox.IsSandboxed)
        {
            MacOSNativeMethods.CGRequestPostEventAccess();
            return;
        }

        MacOSNativeMethods.AXIsProcessTrustedWithOptions(MacOSNativeMethods.GetAccessibilityPromptOptions());
    }

    public static void OpenSettings()
    {
        OpenPrivacyPane("Privacy_Accessibility");
    }

    public static bool IsInputMonitoringGranted() => InputMonitoringState() == PermissionState.Granted;

    public static PermissionState InputMonitoringState() =>
        MacOSNativeMethods.IOHIDCheckAccess(MacOSNativeMethods.HidRequestTypeListenEvent) switch
        {
            MacOSNativeMethods.HidAccessTypeGranted => PermissionState.Granted,
            MacOSNativeMethods.HidAccessTypeDenied => PermissionState.Denied,
            _ => PermissionState.NotAsked
        };

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
        MacWorkspace.OpenUrl($"x-apple.systempreferences:com.apple.preference.security?{pane}");
    }
}
