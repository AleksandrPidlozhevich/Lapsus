using WeCantSpell.Hunspell;

namespace Lapsus.Core.Spelling;

public static class NeighbourWords
{
    private static readonly char[] Separators = [' ', '\t'];

    private static readonly Dictionary<string, HashSet<string>> ListedByLanguage = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object ListedLock = new();

    public static bool IsListed(string? languageCode, string word)
    {
        if (string.IsNullOrEmpty(languageCode))
            return false;

        HashSet<string> listed;
        lock (ListedLock)
        {
            if (!ListedByLanguage.TryGetValue(languageCode, out listed!))
                ListedByLanguage[languageCode] = listed = ReadListed(languageCode);
        }

        return listed.Count > 0 && listed.Contains(word);
    }

    private static HashSet<string> ReadListed(string languageCode)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var name = $"Lapsus.Core.Spelling.Data.{languageCode.ToLowerInvariant()}.neighbour.txt";
        using var stream = typeof(NeighbourWords).Assembly.GetManifestResourceStream(name);
        if (stream is null)
            return words;

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var word = line.Trim();
            if (word.Length > 0 && word[0] != '#')
                words.Add(word);
        }

        return words;
    }

    public static bool IsNeighbours(string word, WordList own, WordList neighbour)
    {
        if (string.IsNullOrEmpty(word))
            return false;

        var lower = word.ToLowerInvariant();
        if (own.Check(word) || own.Check(lower))
            return false;

        if (neighbour.Check(lower))
            return true;

        var capital = char.ToUpperInvariant(lower[0]) + lower[1..];
        return !own.Check(capital) && neighbour.Check(capital);
    }

    public static List<string> Remove(IEnumerable<string> listLines, WordList own, WordList neighbour, out int removed)
    {
        var kept = new List<string>();
        removed = 0;
        foreach (var line in listLines)
        {
            var fields = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && IsNeighbours(fields[0], own, neighbour))
            {
                removed++;
                continue;
            }

            kept.Add(line);
        }

        return kept;
    }
}
