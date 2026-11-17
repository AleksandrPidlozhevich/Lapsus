using System;
using System.Runtime.Versioning;

namespace Lapsus.Input;

// Only the App Nap part is requested: the "allowing idle system sleep" variant, so the Mac still sleeps normally.
// Begin and End must be called on the UI thread.
[SupportedOSPlatform("macos")]
internal static class MacActivity
{
    // NSActivityUserInitiated without NSActivityIdleSystemSleepDisabled (bit 20).
    private const ulong UserInitiatedAllowingIdleSystemSleep = 0x00EF_FFFF;

    private static IntPtr _processInfo;

    private static IntPtr _token;

    internal static bool IsActive => _token != IntPtr.Zero;

    public static void Begin()
    {
        if (_token != IntPtr.Zero)
            return;

        var processInfo = ObjC.Send(ObjC.Class("NSProcessInfo"), ObjC.Selector("processInfo"));
        if (processInfo == IntPtr.Zero)
            return;

        var reason = MacOSNativeMethods.CFStringCreateWithCString(
            IntPtr.Zero, "Keyboard capture is running", MacOSNativeMethods.CFStringEncodingUtf8);
        if (reason == IntPtr.Zero)
            return;

        try
        {
            var activity = ObjC.Send(
                processInfo,
                ObjC.Selector("beginActivityWithOptions:reason:"),
                (IntPtr)(long)UserInitiatedAllowingIdleSystemSleep,
                reason);
            if (activity == IntPtr.Zero)
                return;

            // beginActivity returns an autoreleased token; keep our own reference until End.
            _token = ObjC.Send(activity, ObjC.Selector("retain"));
            _processInfo = processInfo;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(reason);
        }
    }

    public static void End()
    {
        if (_token == IntPtr.Zero)
            return;

        ObjC.Send(_processInfo, ObjC.Selector("endActivity:"), _token);
        ObjC.Send(_token, ObjC.Selector("release"));
        _token = IntPtr.Zero;
        _processInfo = IntPtr.Zero;
    }
}
