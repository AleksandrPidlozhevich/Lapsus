using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Startup;

[SupportedOSPlatform("macos")]
internal static class MacOsDockVisibility
{
    private const long ActivationPolicyAccessory = 1;

    public static void HideFromDock()
    {
        var nsApplicationClass = objc_getClass("NSApplication");
        var sharedApplication = objc_msgSend_IntPtr(nsApplicationClass, sel_registerName("sharedApplication"));
        objc_msgSend_Long(sharedApplication, sel_registerName("setActivationPolicy:"), ActivationPolicyAccessory);
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_Long(IntPtr receiver, IntPtr selector, long arg);
}
