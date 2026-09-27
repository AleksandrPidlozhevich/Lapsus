using System;
using System.Runtime.Versioning;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal static class MacCaretProbe
{

    private const float AxTimeoutSeconds = 0.12f;

    private static readonly IntPtr SystemWideElement = CreateTimedOutSystemWide();

    private static IntPtr CreateTimedOutSystemWide()
    {
        var element = MacOSNativeMethods.AXUIElementCreateSystemWide();
        if (element != IntPtr.Zero)
            MacOSNativeMethods.AXUIElementSetMessagingTimeout(element, AxTimeoutSeconds);

        return element;
    }

    public static CaretBounds Read()
    {
        if (!MacAccessibility.IsProcessTrusted())
            return default;

        var systemWide = SystemWideElement;
        if (systemWide == IntPtr.Zero)
            return default;

        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                systemWide, MacOSNativeMethods.AxFocusedUiElementAttribute, out var focused) != 0
            || focused == IntPtr.Zero)
            return default;

        try
        {
            MacOSNativeMethods.AXUIElementSetMessagingTimeout(focused, AxTimeoutSeconds);
            return CaretOf(focused);
        }
        finally
        {
            MacOSNativeMethods.CFRelease(focused);
        }
    }

    public static string? ForegroundProcessName()
    {
        var pid = FocusedPid();
        return pid > 0 ? MacProcessNames.Read(pid) : null;
    }

    private static int FocusedPid()
    {
        var focus = MacTypingFocusWatcher.ReadFocus(out _);
        return focus.IsEmpty ? 0 : focus.Pid;
    }

    private static CaretBounds CaretOf(IntPtr focused)
    {
        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                focused, MacOSNativeMethods.AxSelectedTextRangeAttribute, out var rangeValue) != 0
            || rangeValue == IntPtr.Zero)
            return default;

        try
        {
            if (!MacOSNativeMethods.AXValueGetCFRange(
                    rangeValue, MacOSNativeMethods.AxValueTypeCfRange, out var range))
                return default;

            if (range.Length == 0)
                return InsertionPoint(focused, range);

            if (!TryBoundsForRange(focused, range, out var rect))
                return default;

            return Sliver(rect, atRight: true);
        }
        finally
        {
            MacOSNativeMethods.CFRelease(rangeValue);
        }
    }

    private static CaretBounds InsertionPoint(IntPtr focused, MacOSNativeMethods.CFRange range)
    {
        var next = new MacOSNativeMethods.CFRange { Location = range.Location, Length = 1 };
        if (TryBoundsForRange(focused, next, out var after))
            return Sliver(after, atRight: false);

        if (range.Location <= 0)
            return default;

        var prev = new MacOSNativeMethods.CFRange { Location = range.Location - 1, Length = 1 };
        return TryBoundsForRange(focused, prev, out var before)
            ? Sliver(before, atRight: true)
            : default;
    }

    private static bool TryBoundsForRange(
        IntPtr focused, MacOSNativeMethods.CFRange range, out MacOSNativeMethods.CGRect rect)
    {
        rect = default;
        var rangeValue = MacOSNativeMethods.AXValueCreate(MacOSNativeMethods.AxValueTypeCfRange, ref range);
        if (rangeValue == IntPtr.Zero)
            return false;

        try
        {
            if (MacOSNativeMethods.AXUIElementCopyParameterizedAttributeValue(
                    focused,
                    MacOSNativeMethods.AxBoundsForRangeParameterizedAttribute,
                    rangeValue,
                    out var boundsValue) != 0
                || boundsValue == IntPtr.Zero)
                return false;

            try
            {
                return MacOSNativeMethods.AXValueGetCGRect(
                           boundsValue, MacOSNativeMethods.AxValueTypeCgRect, out rect)
                       && rect.Size.Height > 0;
            }
            finally
            {
                MacOSNativeMethods.CFRelease(boundsValue);
            }
        }
        finally
        {
            MacOSNativeMethods.CFRelease(rangeValue);
        }
    }

    private static CaretBounds Sliver(MacOSNativeMethods.CGRect rect, bool atRight)
    {
        var left = (int)Math.Round(rect.Origin.X);
        var top = (int)Math.Round(rect.Origin.Y);
        var right = (int)Math.Round(rect.Origin.X + rect.Size.Width);
        var bottom = (int)Math.Round(rect.Origin.Y + rect.Size.Height);
        var x = atRight ? right : left;
        var caret = new CaretBounds(x, top, x + 1, bottom);
        return CaretScreenMapping.IsPlausibleSliver(caret) ? caret : default;
    }
}
