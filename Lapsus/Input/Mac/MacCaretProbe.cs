using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal static class MacCaretProbe
{

    private const float AxTimeoutSeconds = 0.12f;

    private const float ChromiumAxTimeoutSeconds = 0.6f;

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

        EnsureEnhancedAccessibility(focused);

        try
        {
            var chromium = IsChromiumElement(focused);
            MacOSNativeMethods.AXUIElementSetMessagingTimeout(
                focused, chromium ? ChromiumAxTimeoutSeconds : AxTimeoutSeconds);
            var caret = CaretOf(focused);
            if (caret.IsEmpty && chromium)
                caret = CaretInTextDescendants(focused);
            return caret;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(focused);
        }
    }

    private static readonly HashSet<int> EnhancedPids = new();

    private static readonly object EnhancedGate = new();

    private static void EnsureEnhancedAccessibility(IntPtr focused)
    {
        if (MacOSNativeMethods.AXUIElementGetPid(focused, out var pid) != 0 || pid <= 0)
            return;

        lock (EnhancedGate)
        {
            if (!EnhancedPids.Add(pid))
                return;

            if (EnhancedPids.Count > 512)
            {
                EnhancedPids.Clear();
                EnhancedPids.Add(pid);
            }
        }

        if (!ChromiumApps.IsChromiumExecutable(MacProcessNames.ExecutablePath(pid)))
            return;

        var app = MacOSNativeMethods.AXUIElementCreateApplication(pid);
        if (app == IntPtr.Zero)
            return;

        try
        {
            if (MacOSNativeMethods.AXUIElementSetAttributeValue(
                    app, MacOSNativeMethods.AxManualAccessibilityAttribute, MacOSNativeMethods.CFBooleanTrue) != 0)
            {
                lock (EnhancedGate)
                    EnhancedPids.Remove(pid);
            }
        }
        finally
        {
            MacOSNativeMethods.CFRelease(app);
        }
    }

    public static string? ForegroundProcessName()
    {
        var pid = FocusedPid();
        return pid > 0 ? MacProcessNames.Read(pid) : null;
    }

    private static int FocusedPid()
    {
        var focus = MacTypingFocusWatcher.ReadFocusUncached(out _);
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

    private static int _chromiumCheckPid;
    private static bool _chromiumCheck;

    private static bool IsChromiumElement(IntPtr element)
    {
        if (MacOSNativeMethods.AXUIElementGetPid(element, out var pid) != 0 || pid <= 0)
            return false;

        if (pid == _chromiumCheckPid)
            return _chromiumCheck;

        _chromiumCheckPid = pid;
        _chromiumCheck = ChromiumApps.IsChromiumExecutable(MacProcessNames.ExecutablePath(pid));
        return _chromiumCheck;
    }

    // Monaco keeps the editable text below the focused group. A native field already answered above.
    private static CaretBounds CaretInTextDescendants(IntPtr root)
    {
        var pending = new Queue<IntPtr>();
        var depths = new Queue<int>();
        EnqueueChildren(root, 1, pending, depths);

        var seen = 0;
        while (pending.Count > 0 && seen < MaxCaretNodes)
        {
            var element = pending.Dequeue();
            var depth = depths.Dequeue();
            seen++;
            try
            {
                MacOSNativeMethods.AXUIElementSetMessagingTimeout(element, 0.15f);
                var role = ReadRole(element);
                if (role is "AXTextArea" or "AXTextField" or "AXComboBox" or "AXSearchField" or "AXWebArea")
                {
                    var caret = CaretOf(element);
                    if (!caret.IsEmpty)
                    {
                        Drain(pending);
                        return caret;
                    }
                }

                if (depth < MaxCaretDepth
                    && role is "AXGroup" or "AXScrollArea" or "AXSplitGroup" or "AXWebArea" or "AXWindow" or "")
                    EnqueueChildren(element, depth + 1, pending, depths);
            }
            finally
            {
                MacOSNativeMethods.CFRelease(element);
            }
        }

        Drain(pending);
        return default;
    }

    private const int MaxCaretNodes = 40;

    private const int MaxCaretDepth = 5;

    private static void EnqueueChildren(IntPtr element, int depth, Queue<IntPtr> pending, Queue<int> depths)
    {
        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                element, MacOSNativeMethods.AxChildrenAttribute, out var children) != 0
            || children == IntPtr.Zero)
            return;

        try
        {
            var count = MacOSNativeMethods.CFArrayGetCount(children);
            if (count > 20)
                count = 20;

            for (long i = 0; i < count && pending.Count < MaxCaretNodes; i++)
            {
                var child = MacOSNativeMethods.CFArrayGetValueAtIndex(children, i);
                if (child == IntPtr.Zero)
                    continue;

                MacOSNativeMethods.CFRetain(child);
                pending.Enqueue(child);
                depths.Enqueue(depth);
            }
        }
        finally
        {
            MacOSNativeMethods.CFRelease(children);
        }
    }

    private static void Drain(Queue<IntPtr> pending)
    {
        while (pending.Count > 0)
            MacOSNativeMethods.CFRelease(pending.Dequeue());
    }

    private static string ReadRole(IntPtr element)
    {
        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                element, MacOSNativeMethods.AxRoleAttribute, out var role) != 0
            || role == IntPtr.Zero)
            return string.Empty;

        try
        {
            var sb = new StringBuilder(64);
            return MacOSNativeMethods.CFStringGetCString(
                role, sb, sb.Capacity, MacOSNativeMethods.CFStringEncodingUtf8)
                ? sb.ToString()
                : string.Empty;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(role);
        }
    }
}
