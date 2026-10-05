using Avalonia.Threading;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Input;

// Reports when displays are added, removed, moved or change scale, so the layout badge is placed again.
[SupportedOSPlatform("macos")]
internal static class MacDisplayChanges
{
    private const uint BeginConfigurationFlag = 1;

    private static ReconfigurationCallback? _callback;

    private static Action? _onChanged;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReconfigurationCallback(uint display, uint flags, IntPtr userInfo);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern int CGDisplayRegisterReconfigurationCallback(
        ReconfigurationCallback callback, IntPtr userInfo);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern int CGDisplayRemoveReconfigurationCallback(
        ReconfigurationCallback callback, IntPtr userInfo);

    internal static bool IsSubscribed => _callback is not null;

    public static void Subscribe(Action onChanged)
    {
        _onChanged = onChanged;
        if (_callback is not null)
            return;

        // The delegate is kept in a field so the native side never calls a collected function.
        _callback = OnReconfigured;
        CGDisplayRegisterReconfigurationCallback(_callback, IntPtr.Zero);
    }

    public static void Unsubscribe()
    {
        if (_callback is null)
            return;

        CGDisplayRemoveReconfigurationCallback(_callback, IntPtr.Zero);
        _callback = null;
        _onChanged = null;
    }

    private static void OnReconfigured(uint display, uint flags, IntPtr userInfo)
    {
        // Each change is reported twice: when it begins and when it has settled. Act on the second.
        if ((flags & BeginConfigurationFlag) != 0)
            return;

        var action = _onChanged;
        if (action is not null)
            Dispatcher.UIThread.Post(action);
    }
}
