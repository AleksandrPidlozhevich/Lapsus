using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public static class AutoCorrectPolicy
{
    // Short chunks alone flood false crossings; need a line direction.
    public const int MinLength = 3;

    private static readonly HashSet<char> LayerLetterKeys = BuildLayerLetterKeys();

    private static HashSet<char> BuildLayerLetterKeys()
    {
        var keys = new HashSet<char>();
        foreach (var script in Enum.GetValues<Script>())
        {
            if (!Scripts.IsCaseless(script))
                continue;

            var map = ScriptLayouts.FallbackMapFor(script);
            // Slots 0–25 are a–z, so the key is its own slot index.
            for (var letter = 'a'; letter <= 'z'; letter++)
                if (Scripts.Of(map.DeclaredShiftedChar(letter - 'a')) is not null)
                    keys.Add(char.ToUpperInvariant(letter));
        }

        return keys;
    }

    // Digit keys that type a letter on one of these layouts (Armenian Phonetic: 8 = ր).
    public static IReadOnlySet<char> LetterDigits(IEnumerable<KeyboardMap> maps)
    {
        var digits = new HashSet<char>();
        foreach (var map in maps)
            for (var digit = '0'; digit <= '9'; digit++)
                if (BundledKeyboardMaps.En.TryGetSlot(digit, out var slot) && char.IsLetter(map.CharAtSlot(slot)))
                    digits.Add(digit);

        return digits;
    }

    // A typo fix on a word with digits rewrites tokens like h2o or mp3; such a word may only switch layout.
    public static bool KeysOnlyFor(string word, bool keysOnly)
    {
        return keysOnly || HasDigit(word);
    }

    // A word with digits is fixed only when its digit keys read as letters too: x86 is not ղ86.
    public static bool ReadsThrough(string word, string corrected)
    {
        return !HasDigit(word) || !HasDigit(corrected);
    }

    private static bool HasDigit(string text)
    {
        return text.AsSpan().IndexOfAnyInRange('0', '9') >= 0;
    }

    public static bool IsEligible(
        string word, bool lineHasDirection = false, int minLength = MinLength, IReadOnlySet<char>? letterDigits = null)
    {
        var core = TrimSurroundingPunctuation(word);

        // Letterless OEM chunk is a word once the line has crossed.
        if (core.Length == 0 && lineHasDirection && word.Length > 0)
            return word.All(LayoutOemKeys.IsLatinLayoutOem);

        if (core.Length < (lineHasDirection ? 1 : minLength))
            return false;

        var hasLetter = false;
        var hasLower = false;
        var hasUpperAfterFirst = false;

        for (var i = 0; i < core.Length; i++)
        {
            var c = core[i];

            if (char.IsDigit(c))
            {
                if (letterDigits?.Contains(c) is not true)
                    return false;

                continue;
            }

            if (!char.IsLetter(c))
            {
                if (!LayoutOemKeys.IsLatinLayoutOem(c))
                    return false;

                continue;
            }

            hasLetter = true;
            if (char.IsUpper(c))
            {
                // Mid-word capital is camelCase unless this key's shift layer is a letter.
                if (i > 0 && !LayerLetterKeys.Contains(c))
                    hasUpperAfterFirst = true;
            }
            else
            {
                hasLower = true;
            }
        }

        if (!hasLetter)
            return false;

        if (!hasLower)
            return false;

        if (hasUpperAfterFirst)
            return false;

        return true;
    }

    private static ReadOnlySpan<char> TrimSurroundingPunctuation(string word)
    {
        var start = 0;
        var end = word.Length;
        while (start < end && !char.IsLetterOrDigit(word[start])) start++;
        while (end > start && !char.IsLetterOrDigit(word[end - 1])) end--;
        return word.AsSpan(start, end - start);
    }
}
