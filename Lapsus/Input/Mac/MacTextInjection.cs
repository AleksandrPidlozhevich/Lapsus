using System;
using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal static class MacTextInjection
{
    private const float AxTimeoutSeconds = 0.15f;

    public static bool ReplaceTrailing(int count, string text)
    {
        if (TryReplaceViaAccessibility(count, text))
            return true;

        if (count > 0 && SendSelectLeft(count))
            return SendUnicode(text);

        return SendBackspaces(count) && SendUnicode(text);
    }

    private static bool TryReplaceViaAccessibility(int count, string text)
    {
        if (!MacAccessibility.IsProcessTrusted())
            return false;

        var systemWide = MacOSNativeMethods.AXUIElementCreateSystemWide();
        if (systemWide == IntPtr.Zero)
            return false;

        try
        {
            MacOSNativeMethods.AXUIElementSetMessagingTimeout(systemWide, AxTimeoutSeconds);
            if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                    systemWide, MacOSNativeMethods.AxFocusedUiElementAttribute, out var focused) != 0
                || focused == IntPtr.Zero)
                return false;

            try
            {
                MacOSNativeMethods.AXUIElementSetMessagingTimeout(focused, AxTimeoutSeconds);
                return ReplaceFocusedTrailing(focused, count, text);
            }
            finally
            {
                MacOSNativeMethods.CFRelease(focused);
            }
        }
        finally
        {
            MacOSNativeMethods.CFRelease(systemWide);
        }
    }

    private static bool ReplaceFocusedTrailing(IntPtr focused, int count, string text)
    {
        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                focused, MacOSNativeMethods.AxSelectedTextRangeAttribute, out var rangeValue) != 0
            || rangeValue == IntPtr.Zero)
            return false;

        try
        {
            if (!MacOSNativeMethods.AXValueGetCFRange(
                    rangeValue, MacOSNativeMethods.AxValueTypeCfRange, out var range))
                return false;

            if (range.Length != 0)
                return false;

            var end = (int)range.Location;
            if (count < 0 || end < count)
                return false;

            var select = new MacOSNativeMethods.CFRange
            {
                Location = end - count,
                Length = count
            };
            var selectValue = MacOSNativeMethods.AXValueCreate(
                MacOSNativeMethods.AxValueTypeCfRange, ref select);
            if (selectValue == IntPtr.Zero)
                return false;

            try
            {
                if (MacOSNativeMethods.AXUIElementSetAttributeValue(
                        focused, MacOSNativeMethods.AxSelectedTextRangeAttribute, selectValue) != 0)
                    return false;
            }
            finally
            {
                MacOSNativeMethods.CFRelease(selectValue);
            }

            var cfText = MacOSNativeMethods.CFStringCreateWithCString(
                IntPtr.Zero, text ?? string.Empty, MacOSNativeMethods.CFStringEncodingUtf8);
            if (cfText == IntPtr.Zero)
                return false;

            try
            {
                return MacOSNativeMethods.AXUIElementSetAttributeValue(
                           focused, MacOSNativeMethods.AxSelectedTextAttribute, cfText) == 0;
            }
            finally
            {
                MacOSNativeMethods.CFRelease(cfText);
            }
        }
        finally
        {
            MacOSNativeMethods.CFRelease(rangeValue);
        }
    }

    public static bool SendBackspaces(int count)
    {
        if (count <= 0)
            return true;

        for (var i = 0; i < count; i++)
        {
            if (!PostKey(MacOSNativeMethods.DeleteKeyCode, true))
                return false;
            if (!PostKey(MacOSNativeMethods.DeleteKeyCode, false))
                return false;
        }

        return true;
    }

    private static bool SendSelectLeft(int count)
    {
        var shift = MacOSNativeMethods.EventFlagMaskShift;
        for (var i = 0; i < count; i++)
        {
            if (!PostKey(MacOSNativeMethods.LeftArrowKeyCode, true, shift))
                return false;
            if (!PostKey(MacOSNativeMethods.LeftArrowKeyCode, false, shift))
                return false;
        }

        return true;
    }

    public static bool SendUnicode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        var bytes = Encoding.Unicode.GetBytes(text);
        var ev = MacOSNativeMethods.CGEventCreateKeyboardEvent(IntPtr.Zero, 0, true);
        if (ev == IntPtr.Zero)
            return false;

        MacOSNativeMethods.CGEventSetFlags(ev, 0);
        MacOSNativeMethods.CGEventKeyboardSetUnicodeString(ev, text.Length, bytes);
        MarkInjected(ev);
        MacOSNativeMethods.CGEventPost(0, ev);
        MacOSNativeMethods.CFRelease(ev);
        return true;
    }

    public static bool SendCommandChord(ushort keyCode)
    {
        return PostChordKey(keyCode, true) && PostChordKey(keyCode, false);
    }

    private static bool PostChordKey(ushort keyCode, bool keyDown)
    {
        var ev = MacOSNativeMethods.CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, keyDown);
        if (ev == IntPtr.Zero)
            return false;

        MacOSNativeMethods.CGEventSetFlags(ev, MacOSNativeMethods.EventFlagMaskCommand);
        MarkInjected(ev);
        MacOSNativeMethods.CGEventPost(0, ev);
        MacOSNativeMethods.CFRelease(ev);
        return true;
    }

    private static bool PostKey(ushort keyCode, bool keyDown, ulong flags = 0)
    {
        var ev = MacOSNativeMethods.CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, keyDown);
        if (ev == IntPtr.Zero)
            return false;

        MacOSNativeMethods.CGEventSetFlags(ev, flags);
        MarkInjected(ev);
        MacOSNativeMethods.CGEventPost(0, ev);
        MacOSNativeMethods.CFRelease(ev);
        return true;
    }

    private static void MarkInjected(IntPtr ev)
    {
        MacOSNativeMethods.CGEventSetIntegerValueField(
            ev, MacOSNativeMethods.EventSourceUserData, MacOSNativeMethods.InjectedMarker);
    }
}
