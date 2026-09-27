using Lapsus.Core.Layout;

namespace Lapsus.Core.Spelling;

public static class AbjadAffixes
{
    private static readonly string[] HebrewPrefixes =
    [
        "וה", "וב", "ול", "וכ", "ומ", "וש", "כש", "שה", "שב", "של", "מה", "לה", "בה",
        "ו", "ה", "ב", "ל", "כ", "מ", "ש"
    ];

    // One-letter endings omitted on purpose: too many English collisions.
    private static readonly string[] HebrewSuffixes = ["נו", "כם", "הם", "יו", "יה", "ים", "ות", "תי", "נה"];

    private static readonly string[] ArabicPrefixes = ["وال", "بال", "فال", "كال", "لل", "ال", "و", "ف", "ب", "ل", "ك"];

    private static readonly string[] ArabicSuffixes = ["تها", "ها", "هم", "نا", "ات", "ون", "ين", "كم", "هن", "ه", "ك", "ي"];

    // Min stems: 3 Hebrew / 4 Arabic; shorter leaves almost nothing.
    private const int HebrewMinStem = 3;

    private const int ArabicMinStem = 4;

    public static IEnumerable<string> Stems(string word, Script script)
    {
        var (prefixes, suffixes, minStem) = script switch
        {
            Script.Hebrew => (HebrewPrefixes, HebrewSuffixes, HebrewMinStem),
            Script.Arabic => (ArabicPrefixes, ArabicSuffixes, ArabicMinStem),
            _ => ([], [], 0)
        };

        if (prefixes.Length == 0 || word.Length <= minStem)
            yield break;

        foreach (var prefix in prefixes)
            if (word.Length - prefix.Length >= minStem && word.StartsWith(prefix, StringComparison.Ordinal))
                yield return word[prefix.Length..];

        foreach (var suffix in suffixes)
            if (word.Length - suffix.Length >= minStem && word.EndsWith(suffix, StringComparison.Ordinal))
                yield return word[..^suffix.Length];
    }
}
