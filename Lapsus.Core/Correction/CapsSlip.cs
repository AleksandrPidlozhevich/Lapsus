namespace Lapsus.Core.Correction;

// Two slips of the Shift and Caps Lock keys, neither a matter of layout or spelling:
// "пРИВІТ" — Caps Lock was on, so Shift made the first letter small and the rest came out capital;
// "ПРивіт" — Shift was held a letter too long.
internal static class CapsSlip
{
    // shortcut: shorter Caps Lock words are left alone, because iOS, mRNA and eSIM have the same shape;
    // check them against the word list if "tHIS" turns out to matter.
    private const int MinCapsLockLetters = 5;

    public static bool LooksLikeOne(ReadOnlySpan<char> word)
    {
        return IsCapsLockWord(word, MinCapsLockLetters) || IsDoubleCapital(word);
    }

    // A small letter, then capitals only.
    private static bool IsCapsLockWord(ReadOnlySpan<char> word, int minLetters)
    {
        if (word.Length < minLetters || !char.IsLower(word[0]))
            return false;

        foreach (var ch in word[1..])
            if (!char.IsUpper(ch))
                return false;

        return true;
    }

    // Two capitals, then small letters only — but not a plural acronym: IDs, PCs.
    private static bool IsDoubleCapital(ReadOnlySpan<char> word)
    {
        if (word.Length < 3 || !char.IsUpper(word[0]) || !char.IsUpper(word[1]) || word[2..] is "s")
            return false;

        foreach (var ch in word[2..])
            if (!char.IsLower(ch))
                return false;

        return true;
    }

    // The first Caps Lock word shows the key was on; from there to the end of the text every word is turned
    // back, the ones typed without Shift ("ЯК СПРАВИ") included. A word skip answers true for stays.
    public static string UndoCapsLock(string text, Func<string, bool> skip)
    {
        char[]? fixedText = null;
        foreach (var (start, length) in Words(text))
        {
            var word = text.AsSpan(start, length);
            var slipped = IsCapsLockWord(word, fixedText is null ? MinCapsLockLetters : 2);
            if (!slipped && (fixedText is null || length < 2 || !IsAllUpper(word)))
                continue;

            if (skip(word.ToString()))
                continue;

            fixedText ??= text.ToCharArray();
            for (var i = start; i < start + length; i++)
                fixedText[i] = char.ToLowerInvariant(text[i]);
            if (slipped)
                fixedText[start] = char.ToUpperInvariant(text[start]);
        }

        return fixedText is null ? text : new string(fixedText);
    }

    // Only a word known answers true for in small letters: "ПРивіт" is a slip, "MYvar" is somebody's name.
    public static string UndoDoubleCapitals(string text, Func<string, bool> known)
    {
        char[]? fixedText = null;
        foreach (var (start, length) in Words(text))
        {
            var word = text.AsSpan(start, length);
            if (!IsDoubleCapital(word) || !known(word.ToString().ToLowerInvariant()))
                continue;

            fixedText ??= text.ToCharArray();
            fixedText[start + 1] = char.ToLowerInvariant(text[start + 1]);
        }

        return fixedText is null ? text : new string(fixedText);
    }

    private static bool IsAllUpper(ReadOnlySpan<char> word)
    {
        foreach (var ch in word)
            if (!char.IsUpper(ch))
                return false;

        return true;
    }

    // Runs of letters; anything else ends a word.
    private static IEnumerable<(int Start, int Length)> Words(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetter(text[i]))
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start >= 0)
                yield return (start, i - start);
            start = -1;
        }
    }
}
