using System.Text;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Text;

public static class Transliterator
{
    private static readonly (string From, string To)[] CyrillicOut =
    [
        ("а", "a"), ("б", "b"), ("в", "v"), ("г", "g"), ("д", "d"), ("е", "e"), ("ё", "yo"),
        ("ж", "zh"), ("з", "z"), ("и", "i"), ("й", "j"), ("к", "k"), ("л", "l"), ("м", "m"),
        ("н", "n"), ("о", "o"), ("п", "p"), ("р", "r"), ("с", "s"), ("т", "t"), ("у", "u"),
        ("ф", "f"), ("х", "kh"), ("ц", "ts"), ("ч", "ch"), ("ш", "sh"), ("щ", "shch"),
        ("ъ", ""), ("ы", "y"), ("ь", ""), ("э", "e"), ("ю", "yu"), ("я", "ya"),
        ("і", "i"), ("ї", "ji"), ("є", "je"), ("ґ", "g"), ("ў", "w")
    ];

    private static readonly (string From, string To)[] UkrainianOut =
    [
        ("а", "a"), ("б", "b"), ("в", "v"), ("г", "h"), ("ґ", "g"), ("д", "d"), ("е", "e"), ("є", "ie"),
        ("ж", "zh"), ("з", "z"), ("и", "y"), ("і", "i"), ("ї", "i"), ("й", "i"), ("к", "k"), ("л", "l"),
        ("м", "m"), ("н", "n"), ("о", "o"), ("п", "p"), ("р", "r"), ("с", "s"), ("т", "t"), ("у", "u"),
        ("ф", "f"), ("х", "kh"), ("ц", "ts"), ("ч", "ch"), ("ш", "sh"), ("щ", "shch"),
        ("ь", ""), ("ю", "iu"), ("я", "ia"), ("'", "")
    ];

    private static readonly (string From, string To)[] UkrainianOutInitial =
    [
        ("є", "ye"), ("ї", "yi"), ("й", "y"), ("ю", "yu"), ("я", "ya")
    ];

    private static readonly (string From, string To)[] BelarusianOut =
    [
        ("а", "a"), ("б", "b"), ("в", "v"), ("г", "h"), ("д", "d"), ("е", "e"), ("ё", "yo"),
        ("ж", "zh"), ("з", "z"), ("і", "i"), ("й", "y"), ("к", "k"), ("л", "l"), ("м", "m"),
        ("н", "n"), ("о", "o"), ("п", "p"), ("р", "r"), ("с", "s"), ("т", "t"), ("у", "u"), ("ў", "w"),
        ("ф", "f"), ("х", "kh"), ("ц", "ts"), ("ч", "ch"), ("ш", "sh"),
        ("ы", "y"), ("ь", ""), ("э", "e"), ("ю", "yu"), ("я", "ya"), ("'", "")
    ];

    private static readonly (string From, string To)[] BelarusianOutInitial = [("е", "ye")];

    private static readonly (string From, string To)[] BulgarianOut =
    [
        ("а", "a"), ("б", "b"), ("в", "v"), ("г", "g"), ("д", "d"), ("е", "e"), ("ж", "zh"), ("з", "z"),
        ("и", "i"), ("ѝ", "i"), ("й", "y"), ("к", "k"), ("л", "l"), ("м", "m"), ("н", "n"), ("о", "o"),
        ("п", "p"), ("р", "r"), ("с", "s"), ("т", "t"), ("у", "u"), ("ф", "f"), ("х", "h"), ("ц", "ts"),
        ("ч", "ch"), ("ш", "sh"), ("щ", "sht"), ("ъ", "a"), ("ь", "y"), ("ю", "yu"), ("я", "ya")
    ];

    private static readonly (string From, string To)[] MacedonianOut =
    [
        ("а", "a"), ("б", "b"), ("в", "v"), ("г", "g"), ("д", "d"), ("ѓ", "gj"), ("е", "e"), ("ж", "zh"),
        ("з", "z"), ("ѕ", "dz"), ("и", "i"), ("ј", "j"), ("к", "k"), ("л", "l"), ("љ", "lj"), ("м", "m"),
        ("н", "n"), ("њ", "nj"), ("о", "o"), ("п", "p"), ("р", "r"), ("с", "s"), ("т", "t"), ("ќ", "kj"),
        ("у", "u"), ("ф", "f"), ("х", "h"), ("ц", "c"), ("ч", "ch"), ("џ", "dzh"), ("ш", "sh")
    ];

    private static readonly (string From, string To)[] CyrillicIn =
    [
        ("shch", "щ"),
        ("yo", "ё"), ("yu", "ю"), ("ya", "я"),
        ("zh", "ж"), ("kh", "х"), ("ts", "ц"), ("ch", "ч"), ("sh", "ш"),
        ("a", "а"), ("b", "б"), ("c", "ц"), ("d", "д"), ("e", "е"), ("f", "ф"), ("g", "г"),
        ("h", "х"), ("i", "и"), ("j", "й"), ("k", "к"), ("l", "л"), ("m", "м"), ("n", "н"),
        ("o", "о"), ("p", "п"), ("q", "к"), ("r", "р"), ("s", "с"), ("t", "т"), ("u", "у"),
        ("v", "в"), ("w", "в"), ("x", "кс"), ("y", "ы"), ("z", "з")
    ];

    private static readonly (string From, string To)[] GreekOut =
    [
        ("ού", "ou"), ("ου", "ou"),
        ("α", "a"), ("β", "v"), ("γ", "g"), ("δ", "d"), ("ε", "e"), ("ζ", "z"), ("η", "i"),
        ("θ", "th"), ("ι", "i"), ("κ", "k"), ("λ", "l"), ("μ", "m"), ("ν", "n"), ("ξ", "x"),
        ("ο", "o"), ("π", "p"), ("ρ", "r"), ("ς", "s"), ("σ", "s"), ("τ", "t"), ("υ", "y"),
        ("φ", "f"), ("χ", "ch"), ("ψ", "ps"), ("ω", "o"),
        ("ά", "a"), ("έ", "e"), ("ή", "i"), ("ί", "i"), ("ό", "o"), ("ύ", "y"), ("ώ", "o"),
        ("ϊ", "i"), ("ϋ", "y"), ("ΐ", "i"), ("ΰ", "y")
    ];

    private static readonly (string From, string To)[] GreekIn =
    [
        ("ou", "ου"), ("th", "θ"), ("ch", "χ"), ("ps", "ψ"),
        ("a", "α"), ("b", "β"), ("c", "κ"), ("d", "δ"), ("e", "ε"), ("f", "φ"), ("g", "γ"),
        ("h", "η"), ("i", "ι"), ("j", "ι"), ("k", "κ"), ("l", "λ"), ("m", "μ"), ("n", "ν"),
        ("o", "ο"), ("p", "π"), ("q", "κ"), ("r", "ρ"), ("s", "σ"), ("t", "τ"), ("u", "υ"),
        ("v", "β"), ("w", "ω"), ("x", "ξ"), ("y", "υ"), ("z", "ζ")
    ];

    private static readonly (string From, string To)[] HebrewOut =
    [
        ("א", "a"), ("ב", "b"), ("ג", "g"), ("ד", "d"), ("ה", "h"), ("ו", "o"), ("ז", "z"), ("ח", "ch"),
        ("ט", "t"), ("י", "i"), ("כ", "kh"), ("ך", "kh"), ("ל", "l"), ("מ", "m"), ("ם", "m"), ("נ", "n"),
        ("ן", "n"), ("ס", "s"), ("ע", "a"), ("פ", "f"), ("ף", "f"), ("צ", "ts"), ("ץ", "ts"), ("ק", "k"),
        ("ר", "r"), ("ש", "sh"), ("ת", "t"), ("׳", "'"), ("״", "\"")
    ];

    private static readonly (string From, string To)[] HebrewIn =
    [
        ("sh", "ש"), ("ch", "ח"), ("kh", "כ"), ("ts", "צ"), ("tz", "צ"), ("th", "ת"), ("ph", "פ"),
        ("a", ""), ("b", "ב"), ("c", "ק"), ("d", "ד"), ("e", ""), ("f", "פ"), ("g", "ג"), ("h", "ה"),
        ("i", "י"), ("j", "ג"), ("k", "כ"), ("l", "ל"), ("m", "מ"), ("n", "נ"), ("o", "ו"), ("p", "פ"),
        ("q", "ק"), ("r", "ר"), ("s", "ס"), ("t", "ט"), ("u", "ו"), ("v", "ו"), ("w", "ו"), ("x", "קס"),
        ("y", "י"), ("z", "ז")
    ];

    private static readonly (string From, string To)[] HebrewInInitial =
    [
        ("a", "א"), ("e", "א"), ("i", "אי"), ("o", "או"), ("u", "או")
    ];

    private static readonly (string From, string To)[] HebrewInFinal = [("a", "ה"), ("e", "ה")];

    private static readonly (string From, string To)[] HebrewOutFinal = [("ה", "a")];

    private static readonly (string From, string To)[] ArabicOut =
    [
        ("ا", "a"), ("أ", "a"), ("إ", "i"), ("آ", "a"), ("ب", "b"), ("ت", "t"), ("ث", "th"), ("ج", "j"),
        ("ح", "h"), ("خ", "kh"), ("د", "d"), ("ذ", "dh"), ("ر", "r"), ("ز", "z"), ("س", "s"), ("ش", "sh"),
        ("ص", "s"), ("ض", "d"), ("ط", "t"), ("ظ", "z"), ("ع", "'"), ("غ", "gh"), ("ف", "f"), ("ق", "q"),
        ("ك", "k"), ("ل", "l"), ("م", "m"), ("ن", "n"), ("ه", "h"), ("و", "w"), ("ي", "y"), ("ى", "a"),
        ("ة", "a"), ("ء", "'"), ("ؤ", "u"), ("ئ", "i")
    ];

    private static readonly (string From, string To)[] ArabicIn =
    [
        ("sh", "ش"), ("th", "ث"), ("kh", "خ"), ("dh", "ذ"), ("gh", "غ"), ("aa", "ا"), ("ee", "ي"), ("oo", "و"),
        ("2", "ء"), ("3", "ع"), ("5", "خ"), ("6", "ط"), ("7", "ح"), ("8", "ق"), ("9", "ص"),
        ("a", ""), ("b", "ب"), ("c", "ك"), ("d", "د"), ("e", ""), ("f", "ف"), ("g", "ج"), ("h", "ه"),
        ("i", "ي"), ("j", "ج"), ("k", "ك"), ("l", "ل"), ("m", "م"), ("n", "ن"), ("o", "و"), ("p", "ب"),
        ("q", "ق"), ("r", "ر"), ("s", "س"), ("t", "ت"), ("u", "و"), ("v", "ف"), ("w", "و"), ("x", "كس"),
        ("y", "ي"), ("z", "ز")
    ];

    private static readonly (string From, string To)[] ArabicInInitial =
    [
        ("a", "ا"), ("e", "ا"), ("i", "ا"), ("o", "او"), ("u", "او")
    ];

    private static readonly (string From, string To)[] ArabicInFinal = [("a", "ا"), ("e", "ة")];

    public static string Convert(string text, Script latinTarget = Script.Cyrillic, string? language = null)
    {
        return Scripts.Dominant(text) switch
        {
            Script.Cyrillic => Map(text, CyrillicOutFor(language), InitialOutFor(language)),
            Script.Greek => Map(text, GreekOut),
            Script.Hebrew => Map(text, HebrewOut, null, HebrewOutFinal),
            Script.Arabic => Map(text, ArabicOut),
            Script.Latin when latinTarget == Script.Greek => FinalSigma(Map(text, GreekIn)),
            Script.Latin when latinTarget == Script.Hebrew =>
                HebrewFinalForms(Map(text, HebrewIn, HebrewInInitial, HebrewInFinal)),
            Script.Latin when latinTarget == Script.Arabic =>
                Map(text, ArabicIn, ArabicInInitial, ArabicInFinal),
            Script.Latin => Map(text, CyrillicIn),
            _ => text
        };
    }

    private static (string From, string To)[] CyrillicOutFor(string? language)
    {
        return language?.Split('-')[0].ToLowerInvariant() switch
        {
            "uk" => UkrainianOut,
            "be" => BelarusianOut,
            "bg" => BulgarianOut,
            "mk" => MacedonianOut,
            _ => CyrillicOut
        };
    }

    private static (string From, string To)[]? InitialOutFor(string? language)
    {
        return language?.Split('-')[0].ToLowerInvariant() switch
        {
            "uk" => UkrainianOutInitial,
            "be" => BelarusianOutInitial,
            _ => null
        };
    }

    private static string Map(
        string text, (string From, string To)[] table, (string From, string To)[]? initialTable = null,
        (string From, string To)[]? finalTable = null)
    {
        var result = new StringBuilder(text.Length + 8);

        for (var i = 0; i < text.Length;)
        {
            if (initialTable is not null && StartsWord(text, i) &&
                TryMatch(text, i, initialTable, out var openFrom, out var openTo))
            {
                Append(result, text, i, openFrom, openTo);
                i += openFrom.Length;
                continue;
            }

            if (finalTable is not null &&
                TryMatch(text, i, finalTable, out var lastFrom, out var lastTo) &&
                EndsWord(text, i + lastFrom.Length))
            {
                Append(result, text, i, lastFrom, lastTo);
                i += lastFrom.Length;
                continue;
            }

            if (!TryMatch(text, i, table, out var from, out var to))
            {
                result.Append(text[i]);
                i++;
                continue;
            }

            Append(result, text, i, from, to);
            i += from.Length;
        }

        return result.ToString();
    }

    private static void Append(StringBuilder result, string text, int i, string from, string to)
    {
        if (to.Length == 0 || !char.IsUpper(text[i]))
            result.Append(to);
        else if (from.Length > 1 ? char.IsUpper(text[i + 1]) : IsInsideUpperRun(text, i))
            result.Append(to.ToUpperInvariant());
        else
            result.Append(Capitalize(to));
    }

    private static bool StartsWord(string text, int at)
    {
        return at == 0 || (!char.IsLetterOrDigit(text[at - 1]) && text[at - 1] is not ('\'' or '’'));
    }

    private static bool EndsWord(string text, int at)
    {
        return at >= text.Length || (!char.IsLetterOrDigit(text[at]) && text[at] is not ('\'' or '’'));
    }

    private static bool TryMatch(
        string text, int at, (string From, string To)[] table, out string from, out string to)
    {
        foreach (var (candidate, mapped) in table)
        {
            if (at + candidate.Length > text.Length)
                continue;

            if (string.Compare(text, at, candidate, 0, candidate.Length,
                    StringComparison.OrdinalIgnoreCase) != 0)
                continue;

            from = candidate;
            to = mapped;
            return true;
        }

        from = string.Empty;
        to = string.Empty;
        return false;
    }

    private static string FinalSigma(string text)
    {
        var result = new StringBuilder(text);

        for (var i = 0; i < result.Length; i++)
        {
            if (result[i] != 'σ')
                continue;

            if (i + 1 == result.Length || Scripts.Of(result[i + 1]) != Script.Greek)
                result[i] = 'ς';
        }

        return result.ToString();
    }

    private static string HebrewFinalForms(string text)
    {
        const string plain = "כמנפצ";
        const string final = "ךםןףץ";

        var result = new StringBuilder(text);
        for (var i = 0; i < result.Length; i++)
        {
            var at = plain.IndexOf(result[i]);
            if (at >= 0 && (i + 1 == result.Length || Scripts.Of(result[i + 1]) != Script.Hebrew))
                result[i] = final[at];
        }

        return result.ToString();
    }

    private static bool IsInsideUpperRun(string text, int at)
    {
        if (at + 1 < text.Length && char.IsLetter(text[at + 1]))
            return char.IsUpper(text[at + 1]);

        if (at > 0 && char.IsLetter(text[at - 1]))
            return char.IsUpper(text[at - 1]);

        return false;
    }

    private static string Capitalize(string text)
    {
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
