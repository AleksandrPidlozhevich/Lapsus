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
        switch (TryReplaceViaAccessibility(count, text))
        {
            case AxReplace.Replaced:
                return true;
            // The trailing characters are highlighted. Backspacing would delete that whole run
            // on the first key and then keep going. A Unicode event replaces the highlight.
            case AxReplace.Selected:
                return SendUnicode(text, Route.Session);
            default:
                return SendBackspaces(count) && SendUnicode(text, Route.Session);
        }
    }

    private static AxReplace TryReplaceViaAccessibility(int count, string text)
    {
        if (!MacAccessibility.IsProcessTrusted())
            return AxReplace.Miss;

        var systemWide = MacOSNativeMethods.AXUIElementCreateSystemWide();
        if (systemWide == IntPtr.Zero)
            return AxReplace.Miss;

        try
        {
            MacOSNativeMethods.AXUIElementSetMessagingTimeout(systemWide, AxTimeoutSeconds);
            if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                    systemWide, MacOSNativeMethods.AxFocusedUiElementAttribute, out var focused) != 0
                || focused == IntPtr.Zero)
                return AxReplace.Miss;

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

    private static AxReplace ReplaceFocusedTrailing(IntPtr focused, int count, string text)
    {
        if (!TryReadRange(focused, out var range))
            return AxReplace.Miss;

        // A selection the user made is not the trailing run we were asked to replace.
        if (range.Length != 0 || count < 0)
            return AxReplace.Miss;

        if (count == 0)
            return SetSelectedText(focused, text) ? AxReplace.Replaced : AxReplace.Miss;

        if (range.Location < count)
            return AxReplace.Miss;

        var select = new MacOSNativeMethods.CFRange
        {
            Location = range.Location - count,
            Length = count
        };
        if (!TrySetRange(focused, select) || !TryReadRange(focused, out var actual))
            return AxReplace.Miss;

        // Success from the range write is not enough: an empty selection makes the text
        // write insert, and the characters we meant to replace stay in front.
        if (!MacTrailingSelection.Confirmed(
                actual.Location, actual.Length, select.Location, select.Length, ReadSelectedTextLength(focused)))
            return AxReplace.Miss;

        return SetSelectedText(focused, text) ? AxReplace.Replaced : AxReplace.Selected;
    }

    private static bool TryReadRange(IntPtr focused, out MacOSNativeMethods.CFRange range)
    {
        range = default;
        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                focused, MacOSNativeMethods.AxSelectedTextRangeAttribute, out var value) != 0
            || value == IntPtr.Zero)
            return false;

        try
        {
            return MacOSNativeMethods.AXValueGetCFRange(value, MacOSNativeMethods.AxValueTypeCfRange, out range);
        }
        finally
        {
            MacOSNativeMethods.CFRelease(value);
        }
    }

    private static bool TrySetRange(IntPtr focused, MacOSNativeMethods.CFRange range)
    {
        var value = MacOSNativeMethods.AXValueCreate(MacOSNativeMethods.AxValueTypeCfRange, ref range);
        if (value == IntPtr.Zero)
            return false;

        try
        {
            return MacOSNativeMethods.AXUIElementSetAttributeValue(
                       focused, MacOSNativeMethods.AxSelectedTextRangeAttribute, value) == 0;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(value);
        }
    }

    private static int? ReadSelectedTextLength(IntPtr focused)
    {
        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                focused, MacOSNativeMethods.AxSelectedTextAttribute, out var value) != 0
            || value == IntPtr.Zero)
            return null;

        try
        {
            var length = MacOSNativeMethods.CFStringGetLength(value);
            return length < 0 || length > int.MaxValue ? null : (int)length;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(value);
        }
    }

    private static bool SetSelectedText(IntPtr focused, string text)
    {
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

    // Delete posted straight to the process is a private-source key. Electron drops it and still
    // honors Cmd+V, so the word that should have been removed stays and the correction is appended.
    // The session tap is downstream of our HID tap, and these events share it with the paste chord.
    public static bool SendBackspaces(int count)
    {
        if (count <= 0)
            return true;

        for (var i = 0; i < count; i++)
        {
            if (!PostKey(MacOSNativeMethods.DeleteKeyCode, true, route: Route.Session, hidSource: true))
                return false;
            if (!PostKey(MacOSNativeMethods.DeleteKeyCode, false, route: Route.Session, hidSource: true))
                return false;
        }

        return true;
    }

    public static bool SendUnicode(string text, Route route = Route.Hid)
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
        Deliver(route, 0, ev);
        MacOSNativeMethods.CFRelease(ev);
        return true;
    }

    public static bool SendCommandChord(ushort keyCode, int pid = 0)
    {
        // Electron records modifiers from flagsChanged, and only on the modifier key itself.
        // A letter event that merely carries the Command flag is dropped. Delivering straight
        // to the app avoids the process-wide event tap, which sits in front of every HID event.
        return SendChord(keyCode, pid > 0 ? Route.Process : Route.Hid, pid, hidSource: false);
    }

    public static bool SendSessionCommandChord(ushort keyCode)
    {
        return SendChord(keyCode, Route.Session, 0, hidSource: true);
    }

    private static bool SendChord(ushort keyCode, Route route, int pid, bool hidSource)
    {
        var command = (ushort)MacOSNativeMethods.CommandKeyCode;
        var flags = MacOSNativeMethods.EventFlagMaskCommand;
        return PostFlagsChanged(command, flags, route, pid, hidSource)
               && PostKey(keyCode, true, flags, route, pid, hidSource)
               && PostKey(keyCode, false, flags, route, pid, hidSource)
               && PostFlagsChanged(command, 0, route, pid, hidSource);
    }

    private static bool PostKey(
        ushort keyCode, bool keyDown, ulong flags = 0, Route route = Route.Hid, int pid = 0, bool hidSource = false)
    {
        var ev = MacOSNativeMethods.CGEventCreateKeyboardEvent(hidSource ? HidSource() : IntPtr.Zero, keyCode, keyDown);
        if (ev == IntPtr.Zero)
            return false;

        MacOSNativeMethods.CGEventSetFlags(ev, flags);
        if (hidSource)
            StampHidSource(ev);
        MarkInjected(ev);
        Deliver(route, pid, ev);
        MacOSNativeMethods.CFRelease(ev);
        return true;
    }

    private static bool PostFlagsChanged(ushort keyCode, ulong flags, Route route, int pid, bool hidSource)
    {
        var ev = MacOSNativeMethods.CGEventCreateKeyboardEvent(
            hidSource ? HidSource() : IntPtr.Zero, keyCode, flags != 0);
        if (ev == IntPtr.Zero)
            return false;

        MacOSNativeMethods.CGEventSetType(ev, MacOSNativeMethods.EventFlagsChanged);
        MacOSNativeMethods.CGEventSetFlags(ev, flags);
        if (hidSource)
            StampHidSource(ev);
        MarkInjected(ev);
        Deliver(route, pid, ev);
        MacOSNativeMethods.CFRelease(ev);
        return true;
    }

    private static IntPtr _hidSource;

    private static IntPtr HidSource()
    {
        var current = System.Threading.Volatile.Read(ref _hidSource);
        if (current != IntPtr.Zero)
            return current;

        var created = MacOSNativeMethods.CGEventSourceCreate(MacOSNativeMethods.EventSourceStateHidSystem);
        if (created == IntPtr.Zero)
            return IntPtr.Zero;

        var existing = System.Threading.Interlocked.CompareExchange(ref _hidSource, created, IntPtr.Zero);
        if (existing != IntPtr.Zero)
        {
            MacOSNativeMethods.CFRelease(created);
            return existing;
        }

        return created;
    }

    private static void StampHidSource(IntPtr ev)
    {
        MacOSNativeMethods.CGEventSetIntegerValueField(
            ev, MacOSNativeMethods.EventSourceStateId, MacOSNativeMethods.EventSourceStateHidSystem);
    }

    private static void Deliver(Route route, int pid, IntPtr ev)
    {
        if (route == Route.Process && pid > 0)
            MacOSNativeMethods.CGEventPostToPid(pid, ev);
        else if (route == Route.Session)
            MacOSNativeMethods.CGEventPost(MacOSNativeMethods.SessionEventTap, ev);
        else
            MacOSNativeMethods.CGEventPost(0, ev);
    }

    private static void MarkInjected(IntPtr ev)
    {
        MacOSNativeMethods.CGEventSetIntegerValueField(
            ev, MacOSNativeMethods.EventSourceUserData, MacOSNativeMethods.InjectedMarker);
    }

    private enum AxReplace
    {
        Miss,
        Replaced,
        Selected
    }

    internal enum Route
    {
        Hid,
        Session,
        Process
    }
}
