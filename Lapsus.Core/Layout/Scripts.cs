namespace Lapsus.Core.Layout;

public static class Scripts
{
    private static readonly int ScriptCount = Enum.GetValues<Script>().Length;

    public static Script? Of(char ch)
    {
        if (Alphabets.IsLatinLetter(ch)) return Script.Latin;
        if (Alphabets.IsCyrillicLetter(ch)) return Script.Cyrillic;
        if (Alphabets.IsGreekLetter(ch)) return Script.Greek;
        if (Alphabets.IsHebrewLetter(ch)) return Script.Hebrew;
        if (Alphabets.IsArabicLetter(ch)) return Script.Arabic;
        if (Alphabets.IsGeorgianLetter(ch)) return Script.Georgian;
        return null;
    }

    public static bool IsAbjad(Script script)
    {
        return script is Script.Hebrew or Script.Arabic;
    }

    public static bool IsScoringBlind(Script script)
    {
        return IsAbjad(script) || script is Script.Georgian;
    }

    // Scorer cannot tell these apart; a missing source dictionary rigs the comparison.
    public static bool IsBlindPair(Script from, Script to)
    {
        if (IsAbjad(from) || IsAbjad(to))
            return true;

        return (from, to) is (Script.Georgian, Script.Latin) or (Script.Latin, Script.Georgian);
    }

    public static bool IsIgnorableMark(Script script, char ch)
    {
        return Alphabets.IsIgnorableMark(script, ch);
    }

    public static bool IsWordInternalMark(Script script, char ch)
    {
        return Alphabets.IsWordInternalMark(script, ch);
    }

    public static string FoldMarks(string word, Script script)
    {
        if (!IsAbjad(script) && script != Script.Cyrillic)
            return word;

        System.Text.StringBuilder? sb = null;
        for (var i = 0; i < word.Length; i++)
        {
            var ch = word[i];
            var replacement = Alphabets.IsIgnorableMark(script, ch) ? '\0'
                : Alphabets.IsWordInternalMark(script, ch) ? Alphabets.WordInternalMarkKey(ch)
                : ch;
            if (replacement == ch)
            {
                sb?.Append(ch);
                continue;
            }

            sb ??= new System.Text.StringBuilder(word, 0, i, word.Length);
            if (replacement != '\0')
                sb.Append(replacement);
        }

        return sb?.ToString() ?? word;
    }

    public static bool IsWordOf(string word, Script script)
    {
        var letters = 0;
        for (var i = 0; i < word.Length; i++)
        {
            var ch = word[i];
            if (Alphabets.IsLetterOf(script, ch))
            {
                letters++;
                continue;
            }

            if (i == 0)
                return false;

            if (Alphabets.IsIgnorableMark(script, ch))
                continue;

            if (!Alphabets.IsWordInternalMark(script, ch)
                || i == word.Length - 1
                || !Alphabets.IsLetterOf(script, word[i - 1])
                || !Alphabets.IsLetterOf(script, word[i + 1]))
                return false;
        }

        return letters > 0;
    }

    public static bool IsCaseless(Script script)
    {
        return script is Script.Hebrew or Script.Arabic or Script.Georgian;
    }

    public static bool IsMixed(string text)
    {
        Script? seen = null;
        foreach (var ch in text)
        {
            if (Of(ch) is not { } script)
                continue;

            if (seen is null)
                seen = script;
            else if (seen != script)
                return true;
        }

        return false;
    }

    public static Script? Dominant(IEnumerable<char> chars)
    {
        var counts = new int[ScriptCount];
        foreach (var ch in chars)
            if (Of(ch) is { } script)
                counts[(int)script]++;

        var best = -1;
        for (var i = 0; i < counts.Length; i++)
            if (counts[i] > 0 && (best < 0 || counts[i] > counts[best]))
                best = i;

        return best < 0 ? null : (Script)best;
    }
}
