using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

internal static class WordScanner
{
    public static int IndexOfFirstLetter(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
            if (char.IsLetter(text[i]))
                return i;

        return -1;
    }

    public static int EndOfLayoutWord(
        string text, int firstLetter, Script sourceScript, KeyboardMap sourceMap,
        IReadOnlyList<LayoutCandidate> candidates)
    {
        var i = firstLetter;
        while (i < text.Length)
        {
            if (char.IsLetter(text[i]))
            {
                i++;
                continue;
            }

            var runEnd = i;
            while (runEnd < text.Length && !char.IsLetter(text[runEnd])
                                        && (CarriesLetterElsewhere(text[runEnd], sourceMap, candidates)
                                            || IsWordInternalMarkHere(text[runEnd], sourceScript, sourceMap, candidates)
                                            || AccentsTheNextLetter(text, runEnd, sourceMap, candidates)))
                runEnd++;

            if (runEnd == i || runEnd >= text.Length || !char.IsLetter(text[runEnd]))
                break;

            i = runEnd;
        }

        return i;
    }

    public static int EndOfTrailingLayoutRun(
        string text, int wordEnd, KeyboardMap sourceMap, IReadOnlyList<LayoutCandidate> candidates)
    {
        var i = wordEnd;
        while (i < text.Length && !char.IsLetter(text[i])
                               && CarriesLetterElsewhere(text[i], sourceMap, candidates))
            i++;

        return i;
    }

    public static bool CarriesLetterElsewhere(
        char ch, KeyboardMap sourceMap, IReadOnlyList<LayoutCandidate> candidates)
    {
        if (!sourceMap.TryGetKey(ch, out var slot, out var shift))
            return false;

        foreach (var candidate in candidates)
            if (Alphabets.IsLetterOf(candidate.ScoringScript, candidate.Map.CharAtSlot(slot, shift)))
                return true;

        return false;
    }

    public static bool AccentsTheNextLetter(
        string text, int index, KeyboardMap sourceMap, IReadOnlyList<LayoutCandidate> candidates)
    {
        if (index + 1 >= text.Length || !char.IsLetter(text[index + 1]) ||
            !sourceMap.TryGetKey(text[index], out var slot, out var shift))
            return false;

        foreach (var candidate in candidates)
            if (candidate.Map.IsDeadKey(slot, shift))
                return true;

        return false;
    }

    public static bool IsWordInternalMarkHere(
        char ch, Script sourceScript, KeyboardMap sourceMap, IReadOnlyList<LayoutCandidate> candidates)
    {
        if (Alphabets.IsWordInternalMark(sourceScript, ch))
            return true;

        if (!sourceMap.TryGetKey(ch, out var slot, out var shift))
            return false;

        foreach (var candidate in candidates)
            if (Alphabets.IsWordInternalMark(candidate.ScoringScript, candidate.Map.CharAtSlot(slot, shift)))
                return true;

        return false;
    }

    public static string TrimToLetters(string token, Script script)
    {
        var start = 0;
        var end = token.Length;
        while (start < end && !Alphabets.IsLetterOf(script, token[start])) start++;
        while (end > start && !Alphabets.IsLetterOf(script, token[end - 1])) end--;
        return token[start..end];
    }

    public static bool IsAllLetters(string token, Script script)
    {
        return Scripts.IsWordOf(token, script);
    }
}
