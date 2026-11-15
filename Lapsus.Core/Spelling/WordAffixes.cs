using Lapsus.Core.Layout;

namespace Lapsus.Core.Spelling;

public static class WordAffixes
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

    // Shorter than 3 Hebrew / 4 Arabic leaves almost nothing of the stem.
    private const int HebrewMinStem = 3;

    private const int ArabicMinStem = 4;

    // Korean hangs its grammar on the end of the word: 재주 "talent" is also 재주는, 재주가, 재주를, and no
    // frequency list holds every noun with every particle. These are the particles, and the endings of the
    // verb 하다 that turns a noun into a verb (공부 → 공부해요).
    private static readonly string[] KoreanSuffixes =
    [
        "에서는", "으로는", "이에요", "입니다", "이라고", "합니다", "했어요", "하지만",
        "에서", "에게", "으로", "까지", "부터", "한테", "처럼", "보다", "하고", "이나", "이랑", "께서", "에는", "에도",
        "이야", "예요", "이다", "인데", "라고", "들이", "들은", "들을", "들의", "들도",
        "하다", "해요", "했다", "했어", "하는", "해서", "하면", "하지", "한다",
        "은", "는", "이", "가", "을", "를", "에", "의", "도", "로", "와", "과", "만", "들", "랑", "야", "요", "해", "할", "한"
    ];

    // A one-syllable stem plus a particle is any two syllables at all.
    private const int KoreanMinStem = 2;

    public static IEnumerable<string> Stems(string word, Script script)
    {
        var (prefixes, suffixes, minStem) = script switch
        {
            Script.Hebrew => (HebrewPrefixes, HebrewSuffixes, HebrewMinStem),
            Script.Arabic => (ArabicPrefixes, ArabicSuffixes, ArabicMinStem),
            Script.Hangul => ([], KoreanSuffixes, KoreanMinStem),
            _ => ([], [], 0)
        };

        if (suffixes.Length == 0 || word.Length <= minStem)
            yield break;

        foreach (var prefix in prefixes)
            if (word.Length - prefix.Length >= minStem && word.StartsWith(prefix, StringComparison.Ordinal))
                yield return word[prefix.Length..];

        foreach (var suffix in suffixes)
            if (word.Length - suffix.Length >= minStem && word.EndsWith(suffix, StringComparison.Ordinal))
                yield return word[..^suffix.Length];
    }
}
