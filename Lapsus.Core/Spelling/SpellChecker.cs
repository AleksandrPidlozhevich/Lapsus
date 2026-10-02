using System.Globalization;
using Lapsus.Core.Layout;
using WeCantSpell.Hunspell;
using SymSpellEngine = SymSpell;

namespace Lapsus.Core.Spelling;

public readonly record struct DictionarySource(string Code, string Path, Script Script, string? WordFormsPath = null);

public sealed class SpellChecker
{
    static SpellChecker()
    {
        // Hunspell code-page provider must be registered; older .aff encodings are not UTF-8.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    // Don't cut to 1 for short words: false crossings come from keys, not two-edit fixes.
    public const int MaxEditDistance = 2;

    // Index only the commonest words; every list word is still known for lookup.
    public const int DefaultSuggestionIndexSize = 100_000;

    private static readonly char[] Separators = [' ', '\t'];

    private readonly record struct LoadedDictionary(
        Dictionary<string, long> Counts, SymSpellEngine Engine, double LogMaxCount, CharNgramModel? Ngrams,
        Script Script, bool CommaBelow);

    private static readonly Dictionary<string, (long Stamp, LoadedDictionary Loaded)> EngineCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object CacheLock = new();

    private static readonly Dictionary<string, (long Stamp, WordList Words)> LexiconCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, WordList> _lexiconByLanguage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Script, List<WordList>> _lexiconsByScript = new();

    private readonly Dictionary<string, LoadedDictionary> _byLanguage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Script, List<LoadedDictionary>> _byScript = new();
    private readonly Dictionary<Script, List<string>> _codesByScript = new();
    private readonly bool _ngrams;
    private readonly bool _clitics;

    public SpellChecker() : this([])
    {
    }

    public SpellChecker(IEnumerable<DictionarySource> sources, bool ngrams = true, bool clitics = true,
        int suggestionIndexSize = DefaultSuggestionIndexSize, bool lexicon = true,
        int? minLexiconLetters = null)
    {
        _ngrams = ngrams;
        _clitics = clitics;
        _minLexiconLetters = minLexiconLetters;
        foreach (var source in sources)
        {
            if (!TryLoadFile(source.Path, source.Script, source.Code, suggestionIndexSize, out var loaded))
                continue;

            if (lexicon && source.WordFormsPath is { } wordForms && TryLoadLexicon(wordForms) is { } words)
            {
                _lexiconByLanguage[source.Code] = words;
                if (!_lexiconsByScript.TryGetValue(source.Script, out var lexicons))
                    _lexiconsByScript[source.Script] = lexicons = [];
                lexicons.Add(words);
            }

            _byLanguage[source.Code] = loaded;
            if (!_byScript.TryGetValue(source.Script, out var engines))
                _byScript[source.Script] = engines = [];
            engines.Add(loaded);

            if (!_codesByScript.TryGetValue(source.Script, out var codes))
                _codesByScript[source.Script] = codes = [];
            codes.Add(source.Code);
        }
    }

    public bool HasDictionaries => _byLanguage.Count > 0;

    public bool Has(Script script)
    {
        return _byScript.TryGetValue(script, out var engines) && engines.Count > 0;
    }

    public bool HasNgrams(Script script)
    {
        if (!_ngrams || !_byScript.TryGetValue(script, out var engines))
            return false;

        foreach (var loaded in engines)
            if (loaded.Ngrams is not null)
                return true;

        return false;
    }

    public double Naturalness(string word, Script script, string? languageCode = null)
    {
        if (!_ngrams || string.IsNullOrEmpty(word))
            return 0.0;

        var best = 0.0;
        foreach (var loaded in EnginesFor(languageCode, script))
        {
            if (loaded.Ngrams is null)
                continue;

            var score = loaded.Ngrams.Score(LookupKey(word, loaded));
            if (score > best)
                best = score;
        }

        return best;
    }

    public double? LanguageFit(string word, Script script, string languageCode)
    {
        if (!_ngrams || string.IsNullOrEmpty(word) ||
            !_byLanguage.TryGetValue(languageCode, out var loaded) || loaded.Ngrams is null)
            return null;

        return loaded.Ngrams.AverageLogProb(LookupKey(word, loaded));
    }

    private static string LookupKey(string word, in LoadedDictionary loaded)
    {
        var key = LookupKey(word, loaded.Script);
        return loaded.CommaBelow ? FoldCedilla(key) : key;
    }

    // Romanian lists alone: ş/ţ (cedilla) → ș/ț (comma); Turkish ş is its own letter.
    private static string FoldCedilla(string key)
    {
        return key.Replace('ş', 'ș').Replace('ţ', 'ț');
    }

    // Lookups fold marks; written-back text keeps the user's spelling. Hebrew keeps geresh.
    private static string LookupKey(string word, Script script)
    {
        var key = Scripts.FoldMarks(word.ToLowerInvariant(), script);
        if (script == Script.Arabic)
            key = FoldArabic(key);
        else if (script == Script.Cyrillic)
            key = StripInternalMarks(key, script);

        return key;
    }

    // Do not fold alef maqsura to ya — merges short words and hands English strings a hit.
    private static string FoldArabic(string key)
    {
        var folded = key.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا');
        return folded.Length > 1 && folded[^1] == 'ة' ? string.Concat(folded.AsSpan(0, folded.Length - 1), "ه") : folded;
    }

    public double LogCount(string word, Script script, string? languageCode = null)
    {
        if (string.IsNullOrEmpty(word))
            return 0.0;

        var best = 0.0;
        foreach (var loaded in EnginesFor(languageCode, script))
            if (CountOf(loaded, word) is { } count && Math.Log(1 + count) > best)
                best = Math.Log(1 + count);

        return best;
    }

    private static string StripInternalMarks(string word, Script script)
    {
        System.Text.StringBuilder? sb = null;
        for (var i = 0; i < word.Length; i++)
        {
            if (!Scripts.IsWordInternalMark(script, word[i]))
            {
                sb?.Append(word[i]);
                continue;
            }

            sb ??= new System.Text.StringBuilder(word, 0, i, word.Length);
        }

        return sb?.ToString() ?? word;
    }

    public bool IsKnownInLanguage(string word, string languageCode)
    {
        if (string.IsNullOrEmpty(word) || !_byLanguage.TryGetValue(languageCode, out var loaded))
            return false;

        return CountOf(loaded, word) is not null;
    }

    public IReadOnlyList<string> LanguagesFor(Script script)
    {
        return _codesByScript.TryGetValue(script, out var codes) ? codes : [];
    }

    public bool IsKnownWord(string word, Script script, string? languageCode = null)
    {
        if (string.IsNullOrEmpty(word))
            return false;

        foreach (var loaded in EnginesFor(languageCode, script))
            if (CountOf(loaded, word) is not null)
                return true;

        return false;
    }

    // Short strings are where layouts collide; abjad floor is 5 (clitics inflate hits).
    public const int DefaultMinLexiconLetters = 4;

    public const int DefaultMinAbjadLexiconLetters = 5;

    private readonly int? _minLexiconLetters;

    public static int MinLexiconLetters(Script script)
    {
        return Scripts.IsAbjad(script) ? DefaultMinAbjadLexiconLetters : DefaultMinLexiconLetters;
    }

    public bool IsLexiconWord(string word, Script script, string? languageCode = null)
    {
        if (string.IsNullOrEmpty(word) || word.Length < (_minLexiconLetters ?? MinLexiconLetters(script)))
            return false;

        if (languageCode is not null)
            return _lexiconByLanguage.TryGetValue(languageCode, out var own) && Check(own, word);

        if (!_lexiconsByScript.TryGetValue(script, out var lexicons))
            return false;

        foreach (var lexicon in lexicons)
            if (Check(lexicon, word))
                return true;

        return false;
    }

    private static bool Check(WordList lexicon, string word)
    {
        // Keyboard apostrophe / cedilla variants must match dictionary spellings.
        return lexicon.Check(word) ||
               (word.Contains('ʼ') && lexicon.Check(word.Replace('ʼ', '\''))) ||
               (word.AsSpan().IndexOfAny("şţŞŢ") >= 0 &&
                lexicon.Check(word.Replace('ş', 'ș').Replace('ţ', 'ț').Replace('Ş', 'Ș').Replace('Ţ', 'Ț')));
    }

    private static WordList? TryLoadLexicon(string wordFormsPath)
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

    public bool IsKnownStem(string word, Script script, string? languageCode, out double frequency)
    {
        frequency = 0.0;
        if (!_clitics || string.IsNullOrEmpty(word) || !Scripts.IsAbjad(script))
            return false;

        foreach (var stem in AbjadAffixes.Stems(LookupKey(word, script), script))
        foreach (var loaded in EnginesFor(languageCode, script))
        {
            if (CountOf(loaded, stem) is not { } count)
                continue;

            frequency = Normalise(count, loaded.LogMaxCount);
            return true;
        }

        return false;
    }

    public double Frequency(string word, Script script, string? languageCode = null)
    {
        if (string.IsNullOrEmpty(word))
            return 0.0;

        var best = 0.0;
        foreach (var loaded in EnginesFor(languageCode, script))
        {
            if (CountOf(loaded, word) is not { } count)
                continue;

            var frequency = Normalise(count, loaded.LogMaxCount);
            if (frequency > best)
                best = frequency;
        }

        return best;
    }

    public double FrequencyInLanguage(string word, string languageCode)
    {
        if (string.IsNullOrEmpty(word) || !_byLanguage.TryGetValue(languageCode, out var loaded))
            return 0.0;

        return CountOf(loaded, word) is { } count ? Normalise(count, loaded.LogMaxCount) : 0.0;
    }

    // Ukrainian apostrophe words: known when every part between marks is; rarest part sets the count.
    private static long? CountOf(in LoadedDictionary loaded, string word)
    {
        if (loaded.Counts.TryGetValue(LookupKey(word, loaded), out var known))
            return known;

        if (loaded.Script != Script.Cyrillic || !HasInternalMark(word, loaded.Script))
            return null;

        long? rarest = null;
        foreach (var part in Scripts.FoldMarks(word.ToLowerInvariant(), loaded.Script).Split('\''))
        {
            if (part.Length == 0)
                return null;

            if (!loaded.Counts.TryGetValue(LookupKey(part, loaded), out var partCount))
                return null;

            if (rarest is null || partCount < rarest)
                rarest = partCount;
        }

        return rarest;
    }

    private static bool HasInternalMark(string word, Script script)
    {
        foreach (var ch in word)
            if (Scripts.IsWordInternalMark(script, ch))
                return true;

        return false;
    }

    public bool TryCorrect(string word, Script script, out string suggestion, out int distance,
        string? languageCode = null)
    {
        suggestion = word;
        distance = 0;

        if (word.Length < 2)
            return false;

        string? bestTerm = null;
        var bestDistance = int.MaxValue;
        var bestFrequency = 0.0;

        foreach (var loaded in EnginesFor(languageCode, script))
        {
            var hits = loaded.Engine.Lookup(LookupKey(word, loaded), SymSpellEngine.Verbosity.Top, MaxEditDistance);
            if (hits.Count == 0)
                continue;

            var top = hits[0];
            var frequency = Normalise(top.count, loaded.LogMaxCount);

            // Strict ">" so equal distance keeps the language's own list, not load order.
            if (top.distance < bestDistance ||
                (top.distance == bestDistance && frequency > bestFrequency))
            {
                bestDistance = top.distance;
                bestFrequency = frequency;
                bestTerm = top.term;
            }
        }

        if (bestTerm is null)
            return false;

        suggestion = RestoreLeadingCase(word, bestTerm);
        distance = bestDistance;
        return true;
    }

    // Every list word at the closest edit distance, commonest first — for a judge that reads context,
    // where TryCorrect has to commit to one.
    public IReadOnlyList<(string Word, int Distance)> Suggestions(
        string word, Script script, int max, string? languageCode = null)
    {
        if (word.Length < 2 || max <= 0)
            return [];

        var found = new List<(string Term, int Distance, double Frequency)>();
        foreach (var loaded in EnginesFor(languageCode, script))
            foreach (var hit in loaded.Engine.Lookup(LookupKey(word, loaded), SymSpellEngine.Verbosity.Closest,
                         MaxEditDistance))
                if (hit.distance > 0 && !found.Exists(f => f.Term == hit.term))
                    found.Add((hit.term, hit.distance, Normalise(hit.count, loaded.LogMaxCount)));

        // A frequency list of a few tens of thousands words misses most inflected forms ("вихідними",
        // "налаштування"); the Hunspell affixes build them. Asked only when the list has nothing one edit
        // away, since a Ukrainian lookup costs up to a fifth of a second.
        if (!found.Exists(f => f.Distance == 1) && _lexiconsByScript.TryGetValue(script, out var lexicons))
        {
            var lower = word.ToLowerInvariant();
            foreach (var lexicon in lexicons)
                foreach (var suggestion in lexicon.Suggest(lower).Take(max))
                {
                    var term = suggestion.ToLowerInvariant();
                    if (term.Contains(' ') || term.Contains('-') || found.Exists(f => f.Term == term))
                        continue;

                    var distance = Distance(lower, term);
                    if (distance is > 0 and <= MaxEditDistance)
                        found.Add((term, distance, 0.0));
                }
        }

        return found
            .OrderBy(f => f.Distance)
            .ThenByDescending(f => f.Frequency)
            .Take(max)
            .Select(f => (RestoreLeadingCase(word, f.Term), f.Distance))
            .ToList();
    }

    // Edits with adjacent swaps, as SymSpell counts them.
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++)
            d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }

        return d[a.Length, b.Length];
    }

    private IEnumerable<LoadedDictionary> EnginesFor(string? languageCode, Script script)
    {
        if (languageCode is not null && _byLanguage.TryGetValue(languageCode, out var preferred))
            yield return preferred;

        if (!_byScript.TryGetValue(script, out var engines))
            yield break;

        foreach (var loaded in engines)
        {
            if (languageCode is not null &&
                _byLanguage.TryGetValue(languageCode, out var preferredEngine) &&
                ReferenceEquals(loaded.Engine, preferredEngine.Engine))
                continue;

            yield return loaded;
        }
    }

    private static double Normalise(long count, double logMax)
    {
        return logMax <= 0.0 ? 0.0 : Math.Clamp(Math.Log(1 + count) / logMax, 0.0, 1.0);
    }

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

    private static bool TryLoadFile(
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

    private static bool TryBuild(
        string path, Script script, string languageCode, int suggestionIndexSize, out LoadedDictionary loaded)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var commaBelow = string.Equals(languageCode, "ro", StringComparison.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(path))
        {
            if (!TryParse(line, out var word, out var count))
                continue;

            var key = LookupKey(word, script);
            if (commaBelow)
                key = FoldCedilla(key);
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

        var engine = new SymSpellEngine(Math.Min(counts.Count, suggestionIndexSize), MaxEditDistance, 7);
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

    private static string RestoreLeadingCase(string original, string suggestion)
    {
        if (original.Length == 0 || suggestion.Length == 0 || !char.IsUpper(original[0]))
            return suggestion;

        return char.ToUpperInvariant(suggestion[0]) + suggestion[1..];
    }
}
