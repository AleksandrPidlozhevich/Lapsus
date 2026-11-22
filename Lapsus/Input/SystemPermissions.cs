using Lapsus.Audio;
using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Lapsus.Input;

public enum PermissionKind
{
    Accessibility,
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

        // The event tap filters keys, which macOS allows under Accessibility alone; Input Monitoring is for
        // taps that only listen, so Lapsus does not ask for it.
        MacAccessibility.PromptIfNeeded();
    }
}
