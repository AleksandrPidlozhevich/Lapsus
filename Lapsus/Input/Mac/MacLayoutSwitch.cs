using Lapsus.Core.Layout;
using Lapsus.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal static class MacLayoutSwitch
{

    public static string? Apply(
        KeyboardLayout? target, string? layoutId, ref List<InstalledLayout>? layouts)
    {
        var active = MacOSNativeMethods.TISCopyCurrentKeyboardInputSource();
        if (active == IntPtr.Zero)
            return Localizer.Instance["Diag_NoLayoutList"];

        try
        {
            var activeId = MacInstalledLayouts.ReadInputSourceId(active);
            if (TrySwitchTo(layouts, target, activeId, layoutId))
                return null;

            layouts = MacInstalledLayouts.Enumerate().ToList();
            if (layouts.Count == 0)
                return Localizer.Instance["Diag_NoLayoutList"];

            if (TrySwitchTo(layouts, target, activeId, layoutId))
                return null;

            return Localizer.Instance.Format(
                "Diag_LayoutNotInstalled_Mac", target?.ToString() ?? layoutId ?? "?", Describe(layouts));
        }
        finally
        {
            MacOSNativeMethods.CFRelease(active);
        }
    }

    private static bool TrySwitchTo(
        List<InstalledLayout>? layouts, KeyboardLayout? target, string? activeId, string? layoutId)
    {
        if (!string.IsNullOrEmpty(layoutId))
        {
            var exact = layouts?.FirstOrDefault(l => l.LayoutId == layoutId);
            if (exact is not null)
                return exact.LayoutId == activeId || SelectInputSource(exact.LayoutId);
        }

        if (target is null)
            return false;

        var wantedCode = LayoutLanguage.FromKeyboardLayout(target.Value);
        bool Matches(InstalledLayout l) =>
            l.Target == target || (wantedCode is not null && l.LanguageCode == wantedCode);

        var match = layouts?.FirstOrDefault(l => l.LayoutId != activeId && Matches(l))
                    ?? layouts?.FirstOrDefault(Matches);
        if (match is null)
            return false;

        return match.LayoutId == activeId || SelectInputSource(match.LayoutId);
    }

    private static bool SelectInputSource(string layoutId)
    {
        var source = MacInstalledLayouts.ResolveInputSource(layoutId);
        if (source == IntPtr.Zero)
            return false;

        try
        {
            return MacOSNativeMethods.TISSelectInputSource(source) == 0;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(source);
        }
    }

    private static string Describe(IReadOnlyList<InstalledLayout>? layouts)
    {
        if (layouts is not { Count: > 0 })
            return "?";

        var sb = new StringBuilder();
        foreach (var layout in layouts)
        {
            if (layout.Target is null && layout.LanguageCode is null)
                continue;

            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(layout.LanguageCode ?? layout.Target?.ToString() ?? "?");
        }

        return sb.Length == 0 ? "?" : sb.ToString();
    }
}
