using Lapsus.Core.Layout;

namespace Lapsus.Core.Speech;

// Which languages a dictation may be in. Whisper's own detection looks at all 99 of its languages and, on a
// sentence full of English terms, readily decides it is English. So detection only ever chooses among the
// languages the user writes in (their installed layouts), and it has to be sure before it leaves the main one.
public static class DictationLanguagePicker
{
    // Below this, a detected language that is not the main one loses to the main one.
    public const float SwitchConfidence = 0.7f;

    // The Lapsus language codes Whisper can decode. Every catalog language is one of Whisper's.
    private static readonly HashSet<string> Whisper = new(StringComparer.OrdinalIgnoreCase)
    {
        "af", "am", "ar", "as", "az", "ba", "be", "bg", "bn", "bo", "br", "bs", "ca", "cs", "cy", "da", "de",
        "el", "en", "es", "et", "eu", "fa", "fi", "fo", "fr", "gl", "gu", "ha", "haw", "he", "hi", "hr", "ht",
        "hu", "hy", "id", "is", "it", "ja", "jw", "ka", "kk", "km", "kn", "ko", "la", "lb", "ln", "lo", "lt",
        "lv", "mg", "mi", "mk", "ml", "mn", "mr", "ms", "mt", "my", "ne", "nl", "nn", "no", "oc", "pa", "pl",
        "ps", "pt", "ro", "ru", "sa", "sd", "si", "sk", "sl", "sn", "so", "sq", "sr", "su", "sv", "sw", "ta",
        "te", "tg", "th", "tk", "tl", "tr", "tt", "uk", "ur", "uz", "vi", "yi", "yo", "yue", "zh"
    };

    public static bool Supports(string language) => Whisper.Contains(Normalize(language));

    private static readonly Dictionary<string, Script> NonLatin = new(StringComparer.OrdinalIgnoreCase)
    {
        // Whisper writes Serbian in Cyrillic.
        ["ru"] = Script.Cyrillic, ["uk"] = Script.Cyrillic, ["be"] = Script.Cyrillic, ["bg"] = Script.Cyrillic,
        ["mk"] = Script.Cyrillic, ["sr"] = Script.Cyrillic, ["kk"] = Script.Cyrillic, ["tg"] = Script.Cyrillic,
        ["tt"] = Script.Cyrillic, ["ba"] = Script.Cyrillic, ["mn"] = Script.Cyrillic,
        ["el"] = Script.Greek, ["he"] = Script.Hebrew, ["yi"] = Script.Hebrew,
        ["ar"] = Script.Arabic, ["fa"] = Script.Arabic, ["ur"] = Script.Arabic, ["ps"] = Script.Arabic,
        ["sd"] = Script.Arabic, ["ka"] = Script.Georgian,
    };

    private static readonly HashSet<string> Unsupported = new(StringComparer.OrdinalIgnoreCase)
    {
        "am", "as", "bn", "bo", "gu", "hi", "hy", "ja", "km", "kn", "ko", "lo", "ml", "mr", "my", "ne", "pa", "sa",
        "si", "ta", "te", "th", "yue", "zh"
    };

    // The script a dictation in this language comes out in; null for scripts Lapsus does not handle.
    public static Script? ScriptOf(string language)
    {
        var code = Normalize(language);
        if (NonLatin.TryGetValue(code, out var script))
            return script;
        return Unsupported.Contains(code) ? null : Script.Latin;
    }

    // The languages detection may choose from, main one first. A fixed language means no detection at all.
    public static IReadOnlyList<string> Candidates(
        string mainLanguage, IEnumerable<string> userLanguages, string? fixedLanguage = null)
    {
        if (fixedLanguage is not null && Supports(fixedLanguage))
            return [Normalize(fixedLanguage)];

        var list = new List<string>();
        foreach (var code in userLanguages.Prepend(mainLanguage).Select(Normalize))
            if (Supports(code) && !list.Contains(code))
                list.Add(code);

        return list.Count > 0 ? list : ["en"];
    }

    // The language to decode in, given what detection said.
    public static string Resolve(string mainLanguage, string? detected, float probability)
    {
        var main = Normalize(mainLanguage);
        if (detected is null)
            return main;

        var found = Normalize(detected);
        return found == main || probability >= SwitchConfidence ? found : main;
    }

    // "uk-UA" → "uk", "pt_BR" → "pt".
    public static string Normalize(string language)
    {
        var code = language.Trim().ToLowerInvariant();
        var cut = code.IndexOfAny(['-', '_']);
        return cut > 0 ? code[..cut] : code;
    }
}
