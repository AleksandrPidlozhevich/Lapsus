using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Input;

// Stops macOS App Nap from throttling the app while keyboard capture runs. Only the App Nap part is
// requested: the "allowing idle system sleep" variant, so the Mac still sleeps normally.
// Begin and End must be called on the UI thread.
[SupportedOSPlatform("macos")]
internal static class MacActivity
{
    // NSActivityUserInitiated without NSActivityIdleSystemSleepDisabled (bit 20).
    private const ulong UserInitiatedAllowingIdleSystemSleep = 0x00EF_FFFF;

    private const uint Utf8 = 0x08000100;

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    private static IntPtr _processInfo;

    private static IntPtr _token;

    internal static bool IsActive => _token != IntPtr.Zero;

    public static void Begin()
    {
        if (_token != IntPtr.Zero)
            return;

        var processInfo = objc_msgSend(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
        if (processInfo == IntPtr.Zero)
            return;

        var reason = MacOSNativeMethods.CFStringCreateWithCString(
            IntPtr.Zero, "Keyboard capture is running", Utf8);
        if (reason == IntPtr.Zero)
            return;

        try
        {
            var activity = objc_msgSendBegin(
                processInfo,
                sel_registerName("beginActivityWithOptions:reason:"),
                UserInitiatedAllowingIdleSystemSleep,
                reason);
            if (activity == IntPtr.Zero)
                return;

            // beginActivity returns an autoreleased token; keep our own reference until End.
            _token = objc_msgSend(activity, sel_registerName("retain"));
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

        objc_msgSendEnd(_processInfo, sel_registerName("endActivity:"), _token);
        objc_msgSend(_token, sel_registerName("release"));
        _token = IntPtr.Zero;
        _processInfo = IntPtr.Zero;
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSendBegin(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSendEnd(IntPtr receiver, IntPtr selector, IntPtr activity);
}
