using Lapsus.Core.Layout;

namespace Lapsus.Core.Speech;

// Terms the user taught by correcting a dictation: dictated "конфиг для Кубнетис", the user backspaced over the word and
// typed "Kubernetes" — a Latin word that sounds like the one it replaced is a term the recogniser keeps mishearing, so
// "Kubernetes|кубнетис" joins the glossary and the next dictation gets it right. A different word ("Кубнетис" →
// "Docker") is an edit, not a correction, and teaches nothing.
public static class GlossaryLearner
{
    // dictated: the text as dictation left it; corrected: the same span as the field holds it now, read from the keys
    // typed since (Backspace removes, letters add). Both begin where the dictation began.
    public static IReadOnlyList<(string Term, string Heard)> Learn(string dictated, string corrected)
    {
        var same = 0;
        while (same < dictated.Length && same < corrected.Length && dictated[same] == corrected[same])
            same++;
        if (same == dictated.Length)
            return [];

        // From the start of the first word touched.
        var start = same;
        while (start > 0 && char.IsLetterOrDigit(dictated[start - 1]))
            start--;

        // shortcut: word by word in order, so a two-word term («фьючер флэг» → "feature flag") is not learned and an
        // edit that drops or adds a word misaligns the rest; enough for the common fix of one misheard word.
        var removed = Words(dictated[start..]);
        var typed = Words(corrected[start..]);
        var learned = new List<(string, string)>();
        for (var i = 0; i < Math.Min(removed.Length, typed.Length); i++)
            if (Teaches(removed[i], typed[i]))
                learned.Add((typed[i], removed[i].ToLowerInvariant()));
        return learned;
    }

    private static bool Teaches(string heard, string written) =>
        heard != written
        && Scripts.Dominant(written) == Script.Latin && written.Any(char.IsLetter)
        && Scripts.Dominant(heard) is { } script && script != Script.Latin
        && SoundsLike(PhoneticKey.Heard(heard), PhoneticKey.English(written));

    // A few sounds off as the word grows, the same first sound: "kubnetis" and "kubernetes" are the same word misheard.
    private static bool SoundsLike(string heard, string term)
    {
        if (heard.Length < 2 || term.Length < 2 || heard[0] != term[0])
            return false;
        var longest = Math.Max(heard.Length, term.Length);
        var allowed = longest <= 5 ? 1 : longest <= 9 ? 2 : 3;
        return TermRestorer.Distance(heard, term) <= allowed;
    }

    private static string[] Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(',', '.', '!', '?', ':', ';', '«', '»', '"', '(', ')'))
            .Where(w => w.Length > 0)
            .ToArray();
}
