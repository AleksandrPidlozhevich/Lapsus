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

        if (!TryCopyFocused(out var focused))
            return default;

        try
        {
            EnsureEnhancedAccessibility(focused);
            var chromium = IsChromiumElement(focused);
            // The element copied above was built before the manual-accessibility bit. Electron only
            // fills in the document tree after that, so the focused element has to be read again.
            if (chromium && TryCopyFocused(out var refreshed))
            {
                MacOSNativeMethods.CFRelease(focused);
                focused = refreshed;
            }

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

    private static bool TryCopyFocused(out IntPtr focused)
    {
        focused = IntPtr.Zero;
        var systemWide = SystemWideElement;
        if (systemWide == IntPtr.Zero)
            return false;

        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                systemWide, MacOSNativeMethods.AxFocusedUiElementAttribute, out focused) != 0
            || focused == IntPtr.Zero)
        {
            if (focused != IntPtr.Zero)
                MacOSNativeMethods.CFRelease(focused);

            focused = IntPtr.Zero;
            return false;
        }

        return true;
    }

    private static readonly HashSet<int> EnhancedPids = new();

    private static readonly object EnhancedGate = new();

    internal static WebEngineKind PrepareWebEngine(IntPtr focused)
    {
        if (MacOSNativeMethods.AXUIElementGetPid(focused, out var pid) != 0 || pid <= 0)
            return WebEngineKind.None;

        var path = MacProcessNames.ExecutablePath(pid);
        var kind = WebEngineKindOf(path);
        if (kind == WebEngineKind.None)
            return kind;

        lock (EnhancedGate)
        {
            if (!EnhancedPids.Add(pid))
                return kind;

            if (EnhancedPids.Count > 512)
            {
                EnhancedPids.Clear();
                EnhancedPids.Add(pid);
            }
        }

        var enabled = kind switch
        {
            WebEngineKind.Chromium => SetAppFlag(pid, MacOSNativeMethods.AxManualAccessibilityAttribute),
            WebEngineKind.WebKit => SetAppFlag(pid, MacOSNativeMethods.AxEnhancedUserInterfaceAttribute),
            _ => false
        };
        if (!enabled)
        {
            lock (EnhancedGate)
                EnhancedPids.Remove(pid);
        }

        return kind;
    }

    internal enum WebEngineKind
    {
        None,
        Chromium,
        WebKit
    }

    private static WebEngineKind WebEngineKindOf(string? path)
    {
        if (ChromiumApps.IsChromiumExecutable(path))
            return WebEngineKind.Chromium;

        return MacAppIdentity.IsWebKitHost(path) ? WebEngineKind.WebKit : WebEngineKind.None;
    }

    private static bool SetAppFlag(int pid, IntPtr attribute)
    {
        var app = MacOSNativeMethods.AXUIElementCreateApplication(pid);
        if (app == IntPtr.Zero)
            return false;

        try
        {
            return MacOSNativeMethods.AXUIElementSetAttributeValue(
                       app, attribute, MacOSNativeMethods.CFBooleanTrue) == 0;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(app);
        }
    }

    private static void EnsureEnhancedAccessibility(IntPtr focused)
    {
        PrepareWebEngine(focused);
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
    // Depth-first into the document, not across the first children: those are the sidebar.
    private static CaretBounds CaretInTextDescendants(IntPtr root)
    {
        var seen = 0;
        return SearchDescendants(root, 1, ref seen);
    }

    internal static IntPtr CopyNestedTextField(IntPtr root)
    {
        var seen = 0;
        return FindTextField(root, 1, ref seen);
    }

    private const int MaxCaretNodes = 48;

    private const int MaxCaretDepth = 8;

    private const int MaxChildrenPerNode = 48;

    private readonly record struct RankedChild(int Rank, int Index, IntPtr Child, string Role);

    private static CaretBounds SearchDescendants(IntPtr element, int depth, ref int seen)
    {
        var children = CopyRankedChildren(element);
        try
        {
            foreach (var child in children)
            {
                if (seen >= MaxCaretNodes)
                    return default;

                seen++;
                MacOSNativeMethods.AXUIElementSetMessagingTimeout(child.Child, 0.15f);
                if (CaretSearchOrder.IsText(child.Role))
                {
                    var caret = CaretOf(child.Child);
                    if (!caret.IsEmpty)
                        return caret;
                }

                if (depth < MaxCaretDepth && CaretSearchOrder.IsContainer(child.Role))
                {
                    var nested = SearchDescendants(child.Child, depth + 1, ref seen);
                    if (!nested.IsEmpty)
                        return nested;
                }
            }

            return default;
        }
        finally
        {
            foreach (var child in children)
                MacOSNativeMethods.CFRelease(child.Child);
        }
    }

    private static IntPtr FindTextField(IntPtr element, int depth, ref int seen)
    {
        var children = CopyRankedChildren(element);
        try
        {
            foreach (var child in children)
            {
                if (seen >= MaxCaretNodes)
                    return IntPtr.Zero;

                seen++;
                if (child.Role is "AXTextArea" or "AXTextField" or "AXComboBox" or "AXSearchField")
                {
                    MacOSNativeMethods.CFRetain(child.Child);
                    return child.Child;
                }

                if (depth < MaxCaretDepth && CaretSearchOrder.IsContainer(child.Role))
                {
                    var nested = FindTextField(child.Child, depth + 1, ref seen);
                    if (nested != IntPtr.Zero)
                        return nested;
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            foreach (var child in children)
                MacOSNativeMethods.CFRelease(child.Child);
        }
    }

    private static List<RankedChild> CopyRankedChildren(IntPtr element)
    {
        var ranked = new List<RankedChild>();
        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                element, MacOSNativeMethods.AxChildrenAttribute, out var children) != 0
            || children == IntPtr.Zero)
            return ranked;

        try
        {
            var count = MacOSNativeMethods.CFArrayGetCount(children);
            if (count > MaxChildrenPerNode)
                count = MaxChildrenPerNode;

            for (long i = 0; i < count; i++)
            {
                var child = MacOSNativeMethods.CFArrayGetValueAtIndex(children, i);
                if (child == IntPtr.Zero)
                    continue;

                MacOSNativeMethods.CFRetain(child);
                MacOSNativeMethods.AXUIElementSetMessagingTimeout(child, 0.05f);
                var role = ReadRole(child);
                if (!CaretSearchOrder.IsText(role) && !CaretSearchOrder.IsContainer(role))
                {
                    MacOSNativeMethods.CFRelease(child);
                    continue;
                }

                ranked.Add(new RankedChild(CaretSearchOrder.Rank(role), (int)i, child, role));
            }
        }
        finally
        {
            MacOSNativeMethods.CFRelease(children);
        }

        ranked.Sort(static (a, b) =>
        {
            var rank = a.Rank.CompareTo(b.Rank);
            return rank != 0 ? rank : a.Index.CompareTo(b.Index);
        });
        return ranked;
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
