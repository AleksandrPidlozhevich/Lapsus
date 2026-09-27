using Lapsus.Core.Layout;

namespace Lapsus.Core.Spelling;

public static class LanguageAlphabets
{
    // Cyrillic languages differ by a handful of letters — those tell them apart.
    // Serbian omitted on purpose: the offered list is Latin-script.
    private static readonly Dictionary<string, HashSet<char>> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["uk"] = Set("абвгґдеєжзиіїйклмнопрстуфхцчшщьюя"),
        ["be"] = Set("абвгдеёжзійклмнопрстуўфхцчшыьэюя"),
        ["bg"] = Set("абвгдежзийклмнопрстуфхцчшщъьюяѝ"),
        ["ru"] = Set("абвгдеёжзийклмнопрстуфхцчшщъыьэюя"),
        ["mk"] = Set("абвгдѓежзѕијклљмнњопрстќуфхцчџшѐѝ"),
        ["el"] = Set("αβγδεζηθικλμνξοπρστυφχψωςάέήίόύώϊϋΐΰ"),

        // Arabic alphabet excludes Persian/Urdu extras corpora sometimes carry.
        ["he"] = Set("אבגדהוזחטיכךלמםנןסעפףצץקרשת"),
        ["ar"] = Set("ابتثجحخدذرزسشصضطظعغفقكلمنهويءأإآؤئةى")
    };

    private static HashSet<char> Set(string letters)
    {
        return [.. letters];
    }

    public static bool IsWordOf(string word, Script script, string? languageCode)
    {
        if (!ByLanguage.TryGetValue(languageCode ?? string.Empty, out var alphabet) ||
            (alphabet is not null && Scripts.Of(alphabet.First()) != script))
            alphabet = null;

        var letters = 0;
        foreach (var ch in word)
        {
            if (!char.IsLetter(ch))
                continue;

            var lower = char.ToLowerInvariant(ch);
            if (!Alphabets.IsLetterOf(script, lower) || (alphabet is not null && !alphabet.Contains(lower)))
                return false;

            letters++;
        }

        return letters > 0;
    }
}
