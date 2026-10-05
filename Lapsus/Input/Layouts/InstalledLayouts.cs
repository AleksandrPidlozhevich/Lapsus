using Lapsus.Core.Layout;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Input;

[SupportedOSPlatform("windows")]
public static class InstalledLayouts
{

    private static readonly uint[] KeyScanCodes =
    [
        0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32,
        0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C,
        0x0B, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A,
        0x27, 0x0D, 0x33, 0x0C, 0x34, 0x35, 0x29, 0x1A, 0x2B, 0x1B, 0x28,
        0x56
    ];

    public static IReadOnlyList<InstalledLayout> Enumerate()
    {
        var count = (int)NativeMethods.GetKeyboardLayoutList(0, null);
        if (count <= 0)
            return Array.Empty<InstalledLayout>();

        var hkls = new IntPtr[count];
        NativeMethods.GetKeyboardLayoutList(count, hkls);

        var result = new List<InstalledLayout>(count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hkl in hkls)
        {
            var layoutId = LayoutIdFor(hkl);
            if (!seen.Add(layoutId))
                continue;

            var deadKeys = new List<DeadKey>();
            var ligatures = new List<Ligature>();
            var slots = BuildSlots(hkl, false, deadKeys, ligatures);
            var script = Scripts.Dominant(slots);
            var osCode = LayoutLanguageResolver.FromWindowsLangId(hkl);
            var (languageCode, target) = LayoutLanguageResolver.Resolve(script, slots, osCode);

            var shiftedSlots = BuildSlots(hkl, true, deadKeys, ligatures);
            var shifted = script is { } s && Scripts.IsCaseless(s)
                ? shiftedSlots
                : KeyboardMap.ShiftedSymbolsOnly(slots, shiftedSlots);

            result.Add(new InstalledLayout(
                layoutId, hkl, script, languageCode, target,
                new KeyboardMap(slots, shifted, script is Script.Latin or null ? null : deadKeys, ligatures)));
        }

        return result;
    }

    internal static string LayoutIdFor(IntPtr hkl)
    {
        return $"0x{hkl.ToInt64():X}";
    }

    public static bool IsImeLanguage(IntPtr hkl)
    {

        var primaryLanguage = (uint)hkl.ToInt64() & 0x3FF;
        return primaryLanguage is LangChinese or LangJapanese or LangKorean;
    }

    private const uint LangChinese = 0x04;
    private const uint LangJapanese = 0x11;
    private const uint LangKorean = 0x12;

    private static char[] BuildSlots(IntPtr hkl, bool shifted, List<DeadKey> deadKeys, List<Ligature> ligatures)
    {
        var keyState = new byte[256];
        if (shifted)
            keyState[NativeMethods.VK_SHIFT] = 0x80;

        var slots = new char[KeyScanCodes.Length];

        for (var i = 0; i < KeyScanCodes.Length; i++)
        {
            var scan = KeyScanCodes[i];
            var vk = MapVirtualKeyEx(scan, MAPVK_VSC_TO_VK_EX, hkl);
            if (vk == 0)
                continue;

            var sb = new StringBuilder(8);

            var rc = NativeMethods.ToUnicodeEx(vk, scan, keyState, sb, sb.Capacity, 0x4, hkl);
            if (rc < 0 && sb.Length > 0)
                deadKeys.Add(new DeadKey(i, shifted, sb[0]));
            else if (rc > 1)
                ligatures.Add(new Ligature(i, shifted, sb.ToString(0, rc)));

            slots[i] = rc == 1 ? shifted ? sb[0] : char.ToLowerInvariant(sb[0]) : '\0';
        }

        return slots;
    }

    private const uint MAPVK_VSC_TO_VK_EX = 3;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);
}
