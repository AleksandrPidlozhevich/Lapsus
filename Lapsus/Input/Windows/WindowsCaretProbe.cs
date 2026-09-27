using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Input;

[SupportedOSPlatform("windows")]
internal static class WindowsCaretProbe
{
    public static CaretBounds Read()
    {
        var previousDpi = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        try
        {
            return ReadCore();
        }
        finally
        {
            SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    private static CaretBounds ReadCore()
    {

        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(0, ref info) || info.hwndCaret == IntPtr.Zero)
            return default;

        if (IsImeWindow(info.hwndCaret))
            return default;

        if (info.hwndFocus != IntPtr.Zero
            && info.hwndCaret != info.hwndFocus
            && !IsChild(info.hwndFocus, info.hwndCaret))
            return default;

        var client = new CaretBounds(
            info.rcCaret.left, info.rcCaret.top, info.rcCaret.right, info.rcCaret.bottom);

        client = CaretScreenMapping.Scale(client, LogicalToPhysicalScale(info.hwndCaret));
        if (!CaretScreenMapping.IsPlausibleSliver(client))
            return default;

        if (!GetClientRect(info.hwndCaret, out var owner) || owner.right <= 0 || owner.bottom <= 0)
            return default;

        var ownerWidth = owner.right - owner.left;
        var ownerHeight = owner.bottom - owner.top;
        if (!CaretScreenMapping.FitsOwner(client, ownerWidth, ownerHeight))
            return default;

        if (GetAncestor(info.hwndCaret, GA_ROOT) == info.hwndCaret
            && CaretScreenMapping.IsDefaultImeAnchor(client, ownerWidth, ownerHeight))
            return default;

        var origin = new POINT();
        if (!ClientToScreen(info.hwndCaret, ref origin))
            return default;

        return CaretScreenMapping.MapToScreen(client, origin.x, origin.y, ownerWidth, ownerHeight);
    }

    private static double LogicalToPhysicalScale(IntPtr hwnd)
    {
        var awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd));
        if (awareness == DpiAwarenessPerMonitorAware)
            return 1;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero
            || GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) != 0
            || dpiX == 0)
            return 1;

        return dpiX / 96.0;
    }

    private static bool IsImeWindow(IntPtr hwnd)
    {
        var buffer = new StringBuilder(64);
        if (GetClassName(hwnd, buffer, buffer.Capacity) <= 0)
            return false;

        var className = buffer.ToString();
        return className is "IME" or "MSCTFIME UI";
    }
}
