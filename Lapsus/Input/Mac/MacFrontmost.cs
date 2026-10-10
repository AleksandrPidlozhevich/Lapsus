using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Input;

// The process that owns the key window. Hardware key events report the event source, which is not
// the app being typed into, so Chromium detection cannot use that pid.
[SupportedOSPlatform("macos")]
internal static class MacFrontmost
{
    public static int ProcessId()
    {
        // The caret sampler asks from a pool thread, which has no autorelease pool of its own.
        var pool = objc_autoreleasePoolPush();
        try
        {
            var workspace = objc_msgSend_IntPtr(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            if (workspace == IntPtr.Zero)
                return 0;

            var app = objc_msgSend_IntPtr(workspace, sel_registerName("frontmostApplication"));
            if (app == IntPtr.Zero)
                return 0;

            var pid = objc_msgSend_int(app, sel_registerName("processIdentifier"));
            return pid > 0 ? pid : 0;
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern int objc_msgSend_int(IntPtr receiver, IntPtr selector);
}
