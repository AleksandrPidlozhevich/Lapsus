using Avalonia.Controls;
using Lapsus.Input;
using System;
using System.Runtime.Versioning;

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

        ObjC.SendBool(nsWindow, ObjC.Selector("setIgnoresMouseEvents:"), true);
        ObjC.SendBool(nsWindow, ObjC.Selector("setHasShadow:"), false);
        ObjC.SendBool(nsWindow, ObjC.Selector("setHidesOnDeactivate:"), false);
        ObjC.Send(nsWindow, ObjC.Selector("setCollectionBehavior:"), (IntPtr)CollectionBehavior);
        ObjC.Send(nsWindow, ObjC.Selector("setLevel:"), (IntPtr)StatusWindowLevel);
    }

    private static IntPtr AsNsWindow(IntPtr handle)
    {
        var nsWindowClass = ObjC.Class("NSWindow");
        if (nsWindowClass == IntPtr.Zero)
            return IntPtr.Zero;

        if (ObjC.SendReturningBool(handle, ObjC.Selector("isKindOfClass:"), nsWindowClass))
            return handle;

        return ObjC.Send(handle, ObjC.Selector("window"));
    }
}
