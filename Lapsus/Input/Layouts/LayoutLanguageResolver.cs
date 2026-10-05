using Lapsus.Core.Layout;
using System;

namespace Lapsus.Input;

internal static class LayoutLanguageResolver
{
    public static string? FromMacInputSourceId(string? sourceId)
    {
        if (string.IsNullOrEmpty(sourceId))
            return null;

        var id = sourceId.ToLowerInvariant();
        if (id.Contains("belarus")) return "be";
        if (id.Contains("ukrainian")) return "uk";
        if (id.Contains("russian")) return "ru";
        if (id.Contains("bulgarian")) return "bg";
        if (id.Contains("macedonian")) return "mk";
        if (id.Contains("greek")) return "el";
        if (id.Contains("hebrew")) return "he";
        if (id.Contains("arabic")) return "ar";

        if (id.Contains("georgian")) return "ka";
        if (id.Contains("abc") || id.Contains("british") || id.Contains("english") || id.Contains("u.s."))
            return "en";

        return null;
    }

    public static string? FromWindowsLangId(IntPtr hkl)
    {
        var primary = (ushort)(hkl.ToInt64() & 0xFFFF) & 0x3FF;
        return primary switch
        {
            0x09 => "en",
            0x19 => "ru",
            0x22 => "uk",
            0x23 => "be",
            0x02 => "bg",
            0x2F => "mk",
            0x08 => "el",
            0x0D => "he",
            0x01 => "ar",
            0x37 => "ka",
            _ => null
        };
    }

    public static (string? LanguageCode, KeyboardLayout? Target) Resolve(
        Script? script, char[] slots, string? osLanguageCode)
    {
        var code = osLanguageCode;

        if (code is null && script == Script.Cyrillic)
            code = LayoutLanguage.FromCyrillicSlots(slots);

        return (code, LayoutLanguage.ToKeyboardLayout(code));
    }
}
