namespace Lapsus.Core.Layout;

public static class LayoutLanguage
{
    public static KeyboardLayout? ToKeyboardLayout(string? languageCode)
    {
        if (string.IsNullOrEmpty(languageCode))
            return null;

        return languageCode.Split('-')[0] switch
        {
            "en" => KeyboardLayout.En,
            "ru" => KeyboardLayout.Ru,
            "uk" => KeyboardLayout.Uk,
            "be" => KeyboardLayout.Be,
            "bg" => KeyboardLayout.Bg,
            "mk" => KeyboardLayout.Mk,
            "el" => KeyboardLayout.El,
            "he" => KeyboardLayout.He,
            "ar" => KeyboardLayout.Ar,
            "ka" => KeyboardLayout.Ka,
            _ => null
        };
    }

    public static string? FromKeyboardLayout(KeyboardLayout layout)
    {
        return layout switch
        {
            KeyboardLayout.En => "en",
            KeyboardLayout.Ru => "ru",
            KeyboardLayout.Uk => "uk",
            KeyboardLayout.Be => "be",
            KeyboardLayout.Bg => "bg",
            KeyboardLayout.Mk => "mk",
            KeyboardLayout.El => "el",
            KeyboardLayout.He => "he",
            KeyboardLayout.Ar => "ar",
            KeyboardLayout.Ka => "ka",
            _ => null
        };
    }

    public static string? FromCyrillicSlots(ReadOnlySpan<char> slots)
    {
        (string Code, Func<char[]> Factory)[] refs =
        [
            ("be", BundledKeyboardMaps.CreateBeSlots),
            ("uk", BundledKeyboardMaps.CreateUkSlots),
            ("uk", BundledKeyboardMaps.CreateUkAppleSlots),
            ("mk", BundledKeyboardMaps.CreateMkSlots),
            ("bg", BundledKeyboardMaps.CreateBgSlots),
            ("bg", BundledKeyboardMaps.CreateBgPhoneticSlots),
            ("bg", BundledKeyboardMaps.CreateBgBdsSlots),
            ("ru", BundledKeyboardMaps.CreateRuSlots)
        ];

        var bestByCode = new Dictionary<string, int>(5);
        foreach (var (code, factory) in refs)
        {
            var score = SlotAgreement(slots, factory());
            if (!bestByCode.TryGetValue(code, out var prev) || score > prev)
                bestByCode[code] = score;
        }

        string? bestCode = null;
        var best = -1;
        var second = -1;
        foreach (var (code, score) in bestByCode)
            if (score > best)
            {
                second = best;
                best = score;
                bestCode = code;
            }
            else if (score > second)
            {
                second = score;
            }

        const int minScore = 16;
        const int minMargin = 2;
        if (bestCode is null || best < minScore || best - second < minMargin)
            return null;

        return bestCode;
    }

    private static int SlotAgreement(ReadOnlySpan<char> actual, ReadOnlySpan<char> reference)
    {
        var n = Math.Min(actual.Length, reference.Length);
        var score = 0;
        for (var i = 0; i < n; i++)
        {
            var a = actual[i];
            var b = reference[i];
            if (a == '\0' || b == '\0')
                continue;
            if (a == b)
                score++;
        }

        return score;
    }
}
