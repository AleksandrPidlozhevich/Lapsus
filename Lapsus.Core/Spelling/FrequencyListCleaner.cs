using Lapsus.Core.Layout;

namespace Lapsus.Core.Spelling;

// A frequency list is scraped text, and it carries entries no language writes: "нeт" (a Latin "e" among
// Cyrillic), "eсли", "пo". The spell checker counts them as known words, so the fix that should land on them
// never runs. Cleaning drops every entry whose letters span more than one alphabet. It is decided by the
// characters alone, so it needs no lexicon: a Hunspell lexicon is not complete enough to be the authority
// (it lacks many real words), and cleaning by it removed real words and cost typo accuracy in every language.
public static class FrequencyListCleaner
{
    // Rewrites the list without its mixed-alphabet entries, when there are any. Returns how many were removed.
    // Two lists are left as they are. Abjads: their clitic-prefixed words must stay with the spell checker.
    // Latin: the English list's Greek look-alike entries ("yοu", "tο") are high-frequency; removing them moved the
    // English trigram model and cost about a point of English OOV recovery across every target (sweep, Rule A).
    public static int CleanFile(string listPath, Script script)
    {
        if (Scripts.IsAbjad(script) || script == Script.Latin)
            return 0;

        var lines = File.ReadAllLines(listPath);
        var removed = Misspellings(lines);
        if (removed.Count == 0)
            return 0;

        File.WriteAllLines(listPath, lines.Where(line => !removed.Contains(KeyOf(line))));
        return removed.Count;
    }

    public static HashSet<string> Misspellings(IReadOnlyList<string> lines)
    {
        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
            if (KeyOf(line) is { Length: > 0 } word && SpansAlphabets(word))
                removed.Add(word);

        return removed;
    }

    private static bool SpansAlphabets(string word)
    {
        Script? first = null;
        foreach (var ch in word)
        {
            if (!char.IsLetter(ch) || Scripts.Of(ch) is not { } script)
                continue;

            if (first is null)
                first = script;
            else if (script != first)
                return true;
        }

        return false;
    }

    private static string KeyOf(string line)
    {
        var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && long.TryParse(fields[^1], out _) ? fields[0].ToLowerInvariant() : string.Empty;
    }
}
