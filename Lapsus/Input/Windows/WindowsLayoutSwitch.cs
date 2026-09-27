using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Lapsus.Core.Layout;
using Lapsus.Localization;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Input;

[SupportedOSPlatform("windows")]
internal static class WindowsLayoutSwitch
{

    public static string? Apply(
        KeyboardLayout? target, string? layoutId, ref List<InstalledLayout>? layouts)
    {
        var active = ForegroundKeyboardLayout();
        if (TryFindTarget(layouts, target, active, layoutId, out var hkl))
        {
            SwitchTo(hkl);
            return null;
        }

        layouts = InstalledLayouts.Enumerate().ToList();
        if (TryFindTarget(layouts, target, active, layoutId, out hkl))
        {
            SwitchTo(hkl);
            return null;
        }

        return Localizer.Instance.Format(
            "Diag_LayoutNotInstalled", target?.ToString() ?? layoutId ?? "?", Describe(layouts));
    }

    private static bool TryFindTarget(
        List<InstalledLayout>? layouts, KeyboardLayout? target, IntPtr active, string? layoutId, out IntPtr hkl)
    {
        if (!string.IsNullOrEmpty(layoutId))
        {
            var exact = layouts?.FirstOrDefault(l => l.LayoutId == layoutId);
            if (exact is not null)
            {
                hkl = exact.Hkl;
                return true;
            }
        }

        if (target is null)
        {
            hkl = IntPtr.Zero;
            return false;
        }

        var wantedCode = LayoutLanguage.FromKeyboardLayout(target.Value);
        bool Matches(InstalledLayout l) =>
            l.Target == target || (wantedCode is not null && l.LanguageCode == wantedCode);

        var match = layouts?.FirstOrDefault(l => l.Hkl != active && Matches(l))
                     ?? layouts?.FirstOrDefault(Matches);
        if (match is null)
        {
            hkl = IntPtr.Zero;
            return false;
        }

        hkl = match.Hkl;
        return true;
    }

    private static void SwitchTo(IntPtr hkl)
    {
        var hwnd = GetForegroundWindow();
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var thisThread = GetCurrentThreadId();

        var focus = IntPtr.Zero;
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (targetThread != 0 && GetGUIThreadInfo(targetThread, ref info))
            focus = info.hwndFocus;

        if (focus == IntPtr.Zero)
            focus = hwnd;

        var attached = targetThread != 0 && targetThread != thisThread
                       && AttachThreadInput(thisThread, targetThread, true);
        try
        {
            ActivateKeyboardLayout(hkl, KLF_ACTIVATE | KLF_REORDER);
        }
        finally
        {
            if (attached)
                AttachThreadInput(thisThread, targetThread, false);
        }

        RequestLayoutChange(focus, hkl);
        if (hwnd != IntPtr.Zero && hwnd != focus)
            RequestLayoutChange(hwnd, hkl);

        if (ForegroundKeyboardLayout() == hkl)
            return;

        RequestLayoutChange(focus, hkl);
        if (hwnd != IntPtr.Zero && hwnd != focus)
            RequestLayoutChange(hwnd, hkl);
    }

    private static void RequestLayoutChange(IntPtr hwnd, IntPtr hkl)
    {
        if (hwnd == IntPtr.Zero)
            return;

        if (SendMessageTimeout(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl,
                SMTO_ABORTIFHUNG, LayoutSwitchTimeoutMs, out _) == IntPtr.Zero)
            PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
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
