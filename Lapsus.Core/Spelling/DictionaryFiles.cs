using Lapsus.Core.Layout;
using System.Globalization;
using WeCantSpell.Hunspell;
using SymSpellEngine = SymSpell;

namespace Lapsus.Core.Spelling;

// Reads frequency lists and Hunspell files, caching each process-wide until the file changes or is deleted.
internal static class DictionaryFiles
{
    static DictionaryFiles()
    {
        // Hunspell code-page provider must be registered; older .aff encodings are not UTF-8.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    private static readonly char[] Separators = [' ', '\t'];

    private static readonly Dictionary<string, (long Stamp, LoadedDictionary Loaded)> EngineCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object CacheLock = new();

    private static readonly Dictionary<string, (long Stamp, WordList Words)> LexiconCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool CanRead(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadLines(path).Any(line => TryParse(line, out _, out _));
        }
        catch
        {
            return false;
        }
    }

    public static WordList? TryLoadLexicon(string wordFormsPath)
    {
        var affixPath = Path.ChangeExtension(wordFormsPath, ".aff");
        try
        {
            if (!File.Exists(wordFormsPath) || !File.Exists(affixPath))
                return null;

            var stamp = Math.Max(File.GetLastWriteTimeUtc(wordFormsPath).Ticks, File.GetLastWriteTimeUtc(affixPath).Ticks);
            lock (CacheLock)
            {
                if (LexiconCache.TryGetValue(wordFormsPath, out var cached) && cached.Stamp == stamp)
                    return cached.Words;

                var words = WordList.CreateFromFiles(wordFormsPath, affixPath);
                LexiconCache[wordFormsPath] = (stamp, words);
                return words;
            }
        }
        catch
        {
            return null;
        }
    }

    public static bool TryLoad(
        string path, Script script, string languageCode, int suggestionIndexSize, out LoadedDictionary loaded)
    {
        loaded = default;
        try
        {
            if (!File.Exists(path))
                return false;

            var key = $"{path}\n{languageCode}\n{suggestionIndexSize}";
            var stamp = File.GetLastWriteTimeUtc(path).Ticks;
            lock (CacheLock)
            {
                if (EngineCache.TryGetValue(key, out var cached) && cached.Stamp == stamp)
                {
                    loaded = cached.Loaded;
                    return true;
                }

                if (!TryBuild(path, script, languageCode, suggestionIndexSize, out loaded))
                    return false;

                EngineCache[key] = (stamp, loaded);
                PruneDeletedFiles();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParse(string line, out string word, out long count)
    {
        // Space and tab: FrequencyWords and Unilex disagree on the separator.
        var fields = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        word = fields.Length > 0 ? fields[0] : string.Empty;
        count = 0;
        return fields.Length >= 2 &&
               long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) &&
               count > 0;
    }

    private static bool TryBuild(
        string path, Script script, string languageCode, int suggestionIndexSize, out LoadedDictionary loaded)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var commaBelow = string.Equals(languageCode, "ro", StringComparison.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(path))
        {
            if (!TryParse(line, out var word, out var count))
                continue;

            var key = LookupKeys.For(word, script);
            if (commaBelow)
                key = LookupKeys.FoldCedilla(key);
            if (key.Length == 0)
                continue;

            // Skip neighbour-language / wrong-alphabet entries; fold before judging.
            if (!LanguageAlphabets.IsWordOf(key, script, languageCode) || NeighbourWords.IsListed(languageCode, key))
                continue;

            counts[key] = counts.TryGetValue(key, out var earlier) && earlier > long.MaxValue - count
                ? long.MaxValue
                : earlier + count;
        }

        if (counts.Count == 0)
        {
            loaded = default;
            return false;
        }

        var engine = new SymSpellEngine(Math.Min(counts.Count, suggestionIndexSize), SpellChecker.MaxEditDistance, 7);
        var max = 0L;
        foreach (var (word, count) in counts
                     .OrderByDescending(e => e.Value)
                     .ThenBy(e => e.Key, StringComparer.Ordinal)
                     .Take(suggestionIndexSize))
        {
            engine.CreateDictionaryEntry(word, count);
            max = Math.Max(max, count);
        }

        var ngrams = CharNgramModel.Build(counts.Select(e => (e.Key, e.Value)), script);
        loaded = new LoadedDictionary(counts, engine, Math.Log(1 + max), ngrams, script, commaBelow);
        return true;
    }

    private static void PruneDeletedFiles()
    {
        List<string>? gone = null;
        foreach (var key in EngineCache.Keys)
            if (!File.Exists(key[..key.IndexOf('\n')]))
                (gone ??= []).Add(key);

        foreach (var path in LexiconCache.Keys)
            if (!File.Exists(path))
                (gone ??= []).Add(path);

        if (gone is null)
            return;

        foreach (var path in gone)
        {
            EngineCache.Remove(path);
            LexiconCache.Remove(path);
        }
    }
}
