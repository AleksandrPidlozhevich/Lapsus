using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Input;

// Opens a URL the way Finder would, in process. Spawning /usr/bin/open (what Process.Start with
// UseShellExecute does on macOS) is a child process, which the App Sandbox does not reliably allow.
[SupportedOSPlatform("macos")]
internal static class MacWorkspace
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    public static bool OpenUrl(string url)
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var text = objc_msgSend_string(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), url);
            var nsUrl = text == IntPtr.Zero
                ? IntPtr.Zero
                : objc_msgSend_IntPtr(objc_getClass("NSURL"), sel_registerName("URLWithString:"), text);
            if (nsUrl == IntPtr.Zero)
                return false;

            var workspace = objc_msgSend_IntPtr(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            return workspace != IntPtr.Zero
                   && objc_msgSend_bool(workspace, sel_registerName("openURL:"), nsUrl);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjC)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_string(
        IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg);
}
