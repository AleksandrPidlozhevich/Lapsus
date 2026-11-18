using Lapsus.Audio;
using Lapsus.Startup;
using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Lapsus.Input;

public enum PermissionKind
{
    Accessibility,
    InputMonitoring,
    Microphone
}

public enum PermissionState
{
    Granted,
    NotAsked,
    Denied
}

internal static class SystemPermissions
{
    public static bool AccessibilityGranted => State(PermissionKind.Accessibility) == PermissionState.Granted;

    public static bool InputMonitoringGranted =>
        !InputMonitoringNeeded || State(PermissionKind.InputMonitoring) == PermissionState.Granted;

    // In the sandbox the event tap runs on post-event access (the Accessibility list) alone; asking for
    // Input Monitoring there only adds a step and a list entry Lapsus never uses.
    public static bool InputMonitoringNeeded => OperatingSystem.IsMacOS() && !MacSandbox.IsSandboxed;

    public static PermissionState State(PermissionKind kind)
    {
        if (!OperatingSystem.IsMacOS())
            return PermissionState.Granted;

        return kind switch
        {
            // macOS does not tell a refusal from a question never asked here; either way the answer is the list.
            PermissionKind.Accessibility => MacAccessibility.IsProcessTrusted()
                ? PermissionState.Granted
                : PermissionState.NotAsked,
            PermissionKind.InputMonitoring => MacAccessibility.InputMonitoringState(),
            _ => MacMicrophone.Access() switch
            {
                MicrophoneAccess.Granted => PermissionState.Granted,
                MicrophoneAccess.NotAsked => PermissionState.NotAsked,
                _ => PermissionState.Denied
            }
        };
    }

    // Never asked: macOS shows its own dialog. Asked before: that dialog does not come back, so System Settings
    // opens at the list instead. The request can wait on the system, so it stays off the UI thread.
    public static void Request(PermissionKind kind)
    {
        if (OperatingSystem.IsMacOS())
            Task.Run(() => RequestOnMac(kind));
    }

    [SupportedOSPlatform("macos")]
    private static void RequestOnMac(PermissionKind kind)
    {
        var asked = State(kind) != PermissionState.NotAsked;
        switch (kind)
        {
            case PermissionKind.Accessibility:
                // Asking first puts Lapsus into the list; a removed or re-signed app would otherwise be missing from it.
                MacAccessibility.PromptIfNeeded();
                MacAccessibility.OpenSettings();
                break;
            case PermissionKind.InputMonitoring when asked:
                MacAccessibility.OpenInputMonitoringSettings();
                break;
            case PermissionKind.InputMonitoring:
                MacAccessibility.RequestInputMonitoringIfNeeded();
                break;
            case PermissionKind.Microphone when asked:
                MacMicrophone.OpenSettings();
                break;
            case PermissionKind.Microphone:
                MacMicrophone.RequestAccess();
                break;
        }
    }

    public static void PromptForPermissionsIfNeeded()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        MacAccessibility.PromptIfNeeded();
        if (InputMonitoringNeeded)
            MacAccessibility.RequestInputMonitoringIfNeeded();
    }
}
