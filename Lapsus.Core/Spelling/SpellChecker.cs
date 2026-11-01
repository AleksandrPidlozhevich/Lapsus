using Lapsus.Core.Layout;
using WeCantSpell.Hunspell;
using SymSpellEngine = SymSpell;

namespace Lapsus.Core.Spelling;

public readonly record struct DictionarySource(string Code, string Path, Script Script, string? WordFormsPath = null);

public sealed class SpellChecker
{
    // Don't cut to 1 for short words: false crossings come from keys, not two-edit fixes.
    public const int MaxEditDistance = 2;

    // Index only the commonest words; every list word is still known for lookup.
    public const int DefaultSuggestionIndexSize = 100_000;

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
            if (!DictionaryFiles.TryLoad(source.Path, source.Script, source.Code, suggestionIndexSize, out var loaded))
                continue;

            if (lexicon && source.WordFormsPath is { } wordForms && DictionaryFiles.TryLoadLexicon(wordForms) is { } words)
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

    public bool HasLexicon(string languageCode)
    {
        return _lexiconByLanguage.ContainsKey(languageCode);
    }

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

            var score = loaded.Ngrams.Score(loaded.Key(word));
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

        return loaded.Ngrams.AverageLogProb(loaded.Key(word));
    }

    public double LogCount(string word, Script script, string? languageCode = null)
    {
        if (string.IsNullOrEmpty(word))
            return 0.0;

        var best = 0.0;
        foreach (var loaded in EnginesFor(languageCode, script))
            if (loaded.CountOf(word) is { } count && Math.Log(1 + count) > best)
                best = Math.Log(1 + count);

        return best;
    }

    public bool IsKnownInLanguage(string word, string languageCode)
    {
        if (string.IsNullOrEmpty(word) || !_byLanguage.TryGetValue(languageCode, out var loaded))
            return false;

        return loaded.CountOf(word) is not null;
    }

    public IReadOnlyList<string> LanguagesFor(Script script)
    {
        return _codesByScript.TryGetValue(script, out var codes) ? codes : [];
    }

    // In lookup form (lower case, marks folded).
    public IEnumerable<KeyValuePair<string, long>> WordsIn(string languageCode)
    {
        return _byLanguage.TryGetValue(languageCode, out var loaded) ? loaded.Counts : [];
    }

    public bool IsKnownWord(string word, Script script, string? languageCode = null)
    {
        if (string.IsNullOrEmpty(word))
            return false;

        foreach (var loaded in EnginesFor(languageCode, script))
            if (loaded.CountOf(word) is not null)
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

    // typedInScript: no layout crossing is being judged, so the abjad floor (collisions on short strings) is not needed.
    public bool IsLexiconWord(string word, Script script, string? languageCode = null, bool typedInScript = false)
    {
        var floor = _minLexiconLetters ?? (typedInScript ? DefaultMinLexiconLetters : MinLexiconLetters(script));
        if (string.IsNullOrEmpty(word) || word.Length < floor)
            return false;

        if (languageCode is not null)
            return _lexiconByLanguage.TryGetValue(languageCode, out var own) && LookupKeys.InLexicon(own, word);

        if (!_lexiconsByScript.TryGetValue(script, out var lexicons))
            return false;

        foreach (var lexicon in lexicons)
            if (LookupKeys.InLexicon(lexicon, word))
                return true;

        return false;
    }

    public bool IsKnownStem(string word, Script script, string? languageCode, out double frequency)
    {
        frequency = 0.0;
        if (!_clitics || string.IsNullOrEmpty(word) || !Scripts.IsAbjad(script))
            return false;

        foreach (var stem in AbjadAffixes.Stems(LookupKeys.For(word, script), script))
            foreach (var loaded in EnginesFor(languageCode, script))
            {
                if (loaded.CountOf(stem) is not { } count)
                    continue;

                frequency = loaded.Normalise(count);
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
            if (loaded.CountOf(word) is not { } count)
                continue;

            var frequency = loaded.Normalise(count);
            if (frequency > best)
                best = frequency;
        }

        return best;
    }

    public double FrequencyInLanguage(string word, string languageCode)
    {
        if (string.IsNullOrEmpty(word) || !_byLanguage.TryGetValue(languageCode, out var loaded))
            return 0.0;

        return loaded.CountOf(word) is { } count ? loaded.Normalise(count) : 0.0;
    }

    // With a keyboard, spellings at the closest distance are ranked by frequency less the price of the slip
    // that would have typed them, not by frequency alone (a dropped key beats a far one).
    public const double SlipWeight = 0.35;

    public bool TryCorrect(string word, Script script, out string suggestion, out int distance,
        string? languageCode = null, KeyboardMap? map = null)
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
            var hits = loaded.Engine.Lookup(loaded.Key(word),
                map is null ? SymSpellEngine.Verbosity.Top : SymSpellEngine.Verbosity.Closest, MaxEditDistance);
            if (hits.Count == 0)
                continue;

            var top = hits[0];
            var frequency = loaded.Normalise(top.count);
            if (map is not null)
            {
                frequency -= SlipWeight * Correction.TypoCost.Between(word, top.term, map);
                foreach (var hit in hits.Skip(1))
                {
                    var rank = loaded.Normalise(hit.count) -
                               SlipWeight * Correction.TypoCost.Between(word, hit.term, map);
                    if (rank > frequency)
                    {
                        top = hit;
                        frequency = rank;
                    }
                }
            }

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

        suggestion = LookupKeys.RestoreLeadingCase(word, bestTerm);
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
            // "All", not "Closest": for a word the list already holds, Closest returns only the word itself (distance 0).
            foreach (var hit in loaded.Engine.Lookup(loaded.Key(word), SymSpellEngine.Verbosity.All,
                         MaxEditDistance))
                if (hit.distance > 0 && !found.Exists(f => f.Term == hit.term))
                    found.Add((hit.term, hit.distance, loaded.Normalise(hit.count)));

        // Only the nearest spellings, as Closest gave for a word the list does not hold.
        if (found.Count > 0)
        {
            var nearest = found.Min(f => f.Distance);
            found.RemoveAll(f => f.Distance > nearest);
        }

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
            .Select(f => (LookupKeys.RestoreLeadingCase(word, f.Term), f.Distance))
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

    public static bool CanRead(string path)
    {
        return DictionaryFiles.CanRead(path);
    }
}
