using System.Text;

namespace Lapsus.Core.Layout;

internal static class Alphabets
{
    private const char LatinSupplementStart = '\u00C0';
    private const char LatinExtendedBEnd = '\u024F';
    private const char LatinAdditionalStart = '\u1E00';
    private const char LatinAdditionalEnd = '\u1EFF';

    // Latin is not a-z; treating ş/ł/ö as non-letters trimmed them off the word.
    private static readonly HashSet<char> LatinVowels = BuildLatinVowels();

    // ъ is a full vowel in Bulgarian; ѝ/ѐ are stressed и/е.
    private static readonly HashSet<char> CyrillicVowels = new("аеёиоуыэюяіїєўъѝѐ");

    private static readonly HashSet<char> GreekVowels = new("αεηιουωάέήίόύώϊϋΐΰ");

    private static HashSet<char> BuildLatinVowels()
    {
        var vowels = new HashSet<char>("aeiouy") { 'ı', 'ø', 'æ', 'œ', 'ơ', 'ư' };

        try
        {
            AddDecomposingVowels(vowels, LatinSupplementStart, LatinExtendedBEnd);
            AddDecomposingVowels(vowels, LatinAdditionalStart, LatinAdditionalEnd);
        }
        catch (PlatformNotSupportedException)
        {
            // Invariant globalization: no Normalize.
        }

        return vowels;
    }

    private static void AddDecomposingVowels(HashSet<char> vowels, char first, char last)
    {
        for (var c = first; c <= last; c++)
        {
            var lower = char.ToLowerInvariant(c);
            if (!char.IsLetter(lower) || vowels.Contains(lower))
                continue;

            var baseLetter = lower.ToString().Normalize(NormalizationForm.FormD)[0];
            if (vowels.Contains(baseLetter))
                vowels.Add(lower);
        }
    }

    internal static bool IsLatinLetter(char ch)
    {
        var c = char.ToLowerInvariant(ch);
        if (c is >= 'a' and <= 'z')
            return true;

        var inLatinBlock = c is >= LatinSupplementStart and <= LatinExtendedBEnd
            or >= LatinAdditionalStart and <= LatinAdditionalEnd;

        return inLatinBlock && char.IsLetter(c);
    }

    internal static bool IsCyrillicLetter(char ch)
    {
        var c = char.ToLowerInvariant(ch);
        return c is >= '\u0400' and <= '\u04FF';
    }

    internal static bool IsGreekLetter(char ch)
    {
        var c = char.ToLowerInvariant(ch);
        return c is >= '\u0370' and <= '\u03FF' && char.IsLetter(c);
    }

    internal static bool IsHebrewLetter(char ch)
    {
        return ch is >= '\u05D0' and <= '\u05EA';
    }

    private static readonly HashSet<char> GeorgianVowels = new("\u10d0\u10d4\u10d8\u10dd\u10e3");

    private const char ArabicTatweel = '\u0640';

    internal static bool IsArabicLetter(char ch)
    {
        return ch is >= '\u0621' and <= '\u06FF' && ch != ArabicTatweel && char.IsLetter(ch);
    }

    internal static bool IsGeorgianLetter(char ch)
    {
        return ch is >= 'ა' and <= 'ჺ';
    }

    internal static bool IsIgnorableMark(Script script, char ch)
    {
        return script switch
        {
            Script.Hebrew => ch is >= '֑' and <= '֯'
                or >= 'ְ' and <= 'ֽ'
                or 'ֿ' or 'ׁ' or 'ׂ' or 'ׄ' or 'ׅ' or 'ׇ',
            Script.Arabic => ch is >= 'ً' and <= 'ٟ' or 'ٰ' or ArabicTatweel,
            _ => false
        };
    }

    internal static bool IsWordInternalMark(Script script, char ch)
    {
        return script switch
        {
            Script.Cyrillic => ch is '\'' or '’' or 'ʼ',
            Script.Hebrew => ch is '"' or '\'' or '׳' or '״',
            _ => false
        };
    }

    internal static char WordInternalMarkKey(char ch)
    {
        return ch is '"' or '״' ? '"' : '\'';
    }

    // Exhaustive: a new script must be added here; fallthrough used to misclassify as Cyrillic.
    internal static bool IsLetterOf(Script script, char ch)
    {
        return script switch
        {
            Script.Latin => IsLatinLetter(ch),
            Script.Cyrillic => IsCyrillicLetter(ch),
            Script.Greek => IsGreekLetter(ch),
            Script.Hebrew => IsHebrewLetter(ch),
            Script.Arabic => IsArabicLetter(ch),
            Script.Georgian => IsGeorgianLetter(ch),
            _ => false
        };
    }

    internal static bool IsVowelOf(Script script, char ch)
    {
        var c = char.ToLowerInvariant(ch);
        return script switch
        {
            Script.Latin => LatinVowels.Contains(c),
            Script.Cyrillic => CyrillicVowels.Contains(c),
            Script.Greek => GreekVowels.Contains(c),
            Script.Hebrew => false,
            Script.Arabic => false,
            Script.Georgian => GeorgianVowels.Contains(c),
            _ => false
        };
    }
}
