using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace Lapsus.Views;

[SupportedOSPlatform("macos")]
internal static class MacOverlayWindow
{
    private const long CanJoinAllSpaces = 1 << 0;
    private const long Transient = 1 << 3;
    private const long IgnoresCycle = 1 << 6;
    private const long FullScreenAuxiliary = 1 << 8;
    private const long CollectionBehavior = CanJoinAllSpaces | Transient | IgnoresCycle | FullScreenAuxiliary;
    private const long StatusWindowLevel = 25;

    public static void Apply(Window window)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero)
            return;

        var nsWindow = AsNsWindow(handle);
        if (nsWindow == IntPtr.Zero)
            return;

        var ignoresMouse = sel_registerName("setIgnoresMouseEvents:");
        var hasShadow = sel_registerName("setHasShadow:");
        var hides = sel_registerName("setHidesOnDeactivate:");
        objc_msgSend_void_byte(nsWindow, ignoresMouse, 1);
        objc_msgSend_void_byte(nsWindow, hasShadow, 0);
        objc_msgSend_void_byte(nsWindow, hides, 0);
        objc_msgSend_void_long(nsWindow, sel_registerName("setCollectionBehavior:"), CollectionBehavior);
        objc_msgSend_void_long(nsWindow, sel_registerName("setLevel:"), StatusWindowLevel);
    }

    private static IntPtr AsNsWindow(IntPtr handle)
    {
        var nsWindowClass = objc_getClass("NSWindow");
        if (nsWindowClass == IntPtr.Zero)
            return IntPtr.Zero;

        if (objc_msgSend_bool_ptr(handle, sel_registerName("isKindOfClass:"), nsWindowClass))
            return handle;

        return objc_msgSend_IntPtr(handle, sel_registerName("window"));
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_byte(IntPtr receiver, IntPtr selector, byte arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_long(IntPtr receiver, IntPtr selector, long arg);
}
