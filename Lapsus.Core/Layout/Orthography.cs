namespace Lapsus.Core.Layout;

public static class Orthography
{
    private const string HebrewFinalForms = "ךםןףץ";

    public static bool IsPossibleWord(string text, Script script)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && Alphabets.IsLetterOf(script, text[i]))
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start >= 0)
            {
                if (!IsPossibleRun(text.AsSpan(start, i - start), script))
                    return false;

                start = -1;
            }
        }

        return true;
    }

    private static bool IsPossibleRun(ReadOnlySpan<char> word, Script script)
    {
        if (word.Length < 2)
            return true;

        return script switch
        {
            Script.Hebrew => IsPossibleHebrew(word),
            Script.Arabic => IsPossibleArabic(word),
            Script.Greek => IsPossibleGreek(word),
            Script.Cyrillic => IsPossibleCyrillic(word),
            _ => true
        };
    }

    private static bool IsPossibleHebrew(ReadOnlySpan<char> word)
    {
        for (var i = 0; i < word.Length - 1; i++)
            if (HebrewFinalForms.Contains(word[i]))
                return false;

        return true;
    }

    private static bool IsPossibleArabic(ReadOnlySpan<char> word)
    {
        for (var i = 0; i < word.Length - 1; i++)
        {
            if (word[i] == 'ة')
                return false;

            if (word[i] == 'ى' && word[i + 1] != 'ء')
                return false;
        }

        return true;
    }

    private static bool IsPossibleGreek(ReadOnlySpan<char> word)
    {
        for (var i = 0; i < word.Length - 1; i++)
            if (word[i] == 'ς')
                return false;

        return true;
    }

    private static bool IsPossibleCyrillic(ReadOnlySpan<char> word)
    {
        // No word begins with ы or ь. ъ omitted: Bulgarian ъгъл.
        if (char.ToLowerInvariant(word[0]) is 'ы' or 'ь')
            return false;

        for (var i = 1; i < word.Length; i++)
            if (IsSignOrYery(word[i]) && IsSignOrYery(word[i - 1]))
                return false;

        return true;
    }

    private static bool IsSignOrYery(char c)
    {
        return char.ToLowerInvariant(c) is 'ь' or 'ъ' or 'ы';
    }
}
