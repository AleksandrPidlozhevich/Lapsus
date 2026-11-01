using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

internal readonly record struct Reading(string Text, double Prior);

// Every reading of one word, which of them each candidate layout gives, and whether they are only
// spellings of a slip (then the whole rest of the line is context worth reading).
internal sealed record WordReadings(
    List<Reading> All, string?[] ByLayout, bool Spellings = false, string?[]? ByLigature = null)
{
    public double PriorOf(string text)
    {
        var at = All.FindIndex(r => r.Text == text);
        return at < 0 ? 0.0 : All[at].Prior;
    }
}

// Builds the readings ReadingRanker offers the model for one word: as typed, the dictionary's pick, each
// layout's remap and keyboard-priced spellings, each with a prior in nats.
internal sealed class WordReadingsBuilder(IPhraseCorrector? adviser, IReadOnlySet<string>? spellingLanguages)
{
    // Priors in nats of model log-probability, added to what the model says.
    internal const double KeepBias = 1.0;

    // On top of KeepBias for a word the dictionary knows as typed: a small model reads Ukrainian or
    // Hebrew less fluently than English, and must not trade "тій" for "ns". Two letters are known in
    // every list ("ps", "yt"), so they earn little.
    internal const double KnownBias = 3.0;

    internal const double ShortKnownBias = 0.5;

    // For a form only Hunspell builds: a short frequency list lacks most inflections, and "вихідними"
    // must not become the listed "вигідними".
    internal const double LexiconBias = 2.0;

    // For a word no list knows that the dictionary still left alone: most are real words a short list
    // lacks ("ტორტი", "напішы"), and the model would trade them for a commoner neighbour ("პორტი").
    internal const double KeptUnknownBias = 3.0;

    private const int MinLettersForKnownBias = 3;

    internal const double AdviserBias = 2.0;

    // Per unit of TypoCost: a neighbouring key is 0.6 of one, a key across the board 1.4.
    internal const double EditPenalty = 2.0;

    internal const double SpellingAdviserBias = 2.5;

    private const int MaxReadingsPerWord = 10;

    // Of the dictionary's spellings for a slip, the ones a finger most likely produced it from.
    private const int MaxSpellingsPerWord = 5;

    private static readonly char[] GluedPunctuation = ['.', ',', '!', '?', ';', ':'];

    public WordReadings Build(
        string typed,
        string? advised,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        bool lineSwitched)
    {
        var readings = new List<Reading>();
        var byLayout = new string?[candidates.Count];

        void Add(string reading, double prior)
        {
            if (reading.Length == 0 || readings.Count >= MaxReadingsPerWord)
                return;

            var at = readings.FindIndex(r => r.Text == reading);
            if (at < 0)
                readings.Add(new Reading(reading, prior));
            else if (prior > readings[at].Prior)
                readings[at] = new Reading(reading, prior);
        }

        void AddSpellFixes(string word, KeyboardMap map, string? language)
        {
            var (lead, core, trail) = Split(word);
            if (core.Length == 0 || Scripts.Dominant(core) is not { } script || !ModelSpellsIn(language) ||
                adviser is not { IsReady: true } dictionary || !dictionary.Knows(script))
                return;

            var spellings = dictionary.SpellSuggestions(core, script)
                .Select(s => (s.Word, Cost: TypoCost.Between(core, s.Word, map)))
                .OrderBy(s => s.Cost)
                .Take(MaxSpellingsPerWord);
            foreach (var (fix, cost) in spellings)
                Add(lead + fix + trail, -EditPenalty * cost);
        }

        // An unknown word the dictionary spell-fixed in its own script is a slip, not a layout: the model
        // picks among spellings, and may neither keep the non-word nor carry it off to another script — it
        // reads Greek and Ukrainian less well than English, and would take "πρόεδε" or "днллю" → "look.".
        // Only a plain word: "config.json" is no slip, whatever the dictionary makes of "json".
        var fixedInPlace = !lineSwitched && advised is not null && advised != typed && !Knows(typed) &&
                           Scripts.Dominant(advised) == Scripts.Dominant(typed) &&
                           Split(typed).Core.All(char.IsLetter);
        var source = MapFor(typed, active, installed, candidates);
        var language = LanguageOf(typed, active, installed);

        // Where the model reads too poorly to choose a spelling, the dictionary's stands without asking.
        if (fixedInPlace && !ModelSpellsIn(language))
            return new WordReadings([new Reading(advised!, AdviserBias)], byLayout);
        if (!fixedInPlace)
            Add(typed, KeepBias + (Knows(typed) ? KnownBiasOf(typed)
                : Accepts(typed) ? LexiconBias
                : advised is null || advised == typed ? KeptUnknownBias
                : 0.0));

        // The dictionary picks among spellings by frequency alone; priced like the rest by the slip it
        // assumes, its pick keeps only a small head start.
        if (advised is not null)
            Add(advised, fixedInPlace && source is not null
                ? SpellingAdviserBias - EditPenalty * TypoCost.Between(Split(typed).Core, Split(advised).Core, source)
                : AdviserBias);

        var remaps = new List<string>();
        var remapMaps = new List<(KeyboardMap Map, string? Language)>();
        string?[]? byLigature = null;
        if (!fixedInPlace && source is not null)
            for (var c = 0; c < candidates.Count; c++)
            {
                var candidate = candidates[c];
                if (ReferenceEquals(candidate.Map, source))
                    continue;

                var remap = LayoutTranscoder.Transcode(typed, source, candidate.Map);
                // A letter typed is not a comma meant; only a comma key may turn out to be a letter.
                if (remap == typed || (HasLetter(typed) && !HasLetter(remap)))
                    continue;

                byLayout[c] = remap;
                remaps.Add(remap);
                remapMaps.Add((candidate.Map, candidate.LanguageCode));
                Add(remap, 0.0);

                // The Arabic b key types "لا", which reads back as g then h; "لاعل" is "bug" too.
                if (source.HasLigatures &&
                    LayoutTranscoder.TranscodeLigaturesAsKeys(typed, source, candidate.Map) is var keyed &&
                    keyed != remap && HasLetter(keyed))
                {
                    byLigature ??= new string?[candidates.Count];
                    byLigature[c] = keyed;
                    remaps.Add(keyed);
                    remapMaps.Add((candidate.Map, candidate.LanguageCode));
                    Add(keyed, 0.0);
                }

                // "nfr," is "так," as often as it is "такб".
                if (typed.Length > 1 && Array.IndexOf(GluedPunctuation, typed[^1]) >= 0)
                    Add(LayoutTranscoder.Transcode(typed[..^1], source, candidate.Map) + typed[^1], 0.0);
            }

        // A spelling the dictionary accepts is not respelled: in Georgian or Belarusian the model reads a
        // commoner neighbour as far likelier ("ტორტი" → "პორტი", "напішы" → "напіша"), and the dictionary
        // brain never respells one either.
        if (source is not null && !Accepts(typed))
            AddSpellFixes(typed, source, language);

        // A remap of keys that are not all letters would spell-fix a comma away.
        if (typed.All(char.IsLetter))
            for (var r = 0; r < remaps.Count; r++)
                AddSpellFixes(remaps[r], remapMaps[r].Map, remapMaps[r].Language);

        return new WordReadings(readings, byLayout, fixedInPlace, byLigature);
    }

    private bool ModelSpellsIn(string? language)
    {
        return spellingLanguages is null ||
               (language is not null && spellingLanguages.Contains(language.Split('-')[0]));
    }

    // The language of the layout the word was typed on: the active one, or an installed one of its script.
    private static string? LanguageOf(string word, LayoutSource active, IReadOnlyList<LayoutSource> installed)
    {
        var script = Scripts.Dominant(word);
        if (script is null || script == active.Script)
            return active.LanguageCode;

        foreach (var layout in installed)
            if (layout.Script == script)
                return layout.LanguageCode;

        return null;
    }

    private double KnownBiasOf(string word)
    {
        if (!Knows(word))
            return 0.0;

        var script = Scripts.Dominant(word)!.Value;
        return WordScanner.TrimToLetters(word, script).Length >= MinLettersForKnownBias ? KnownBias : ShortKnownBias;
    }
    private bool Accepts(string word)
    {
        return adviser is { IsReady: true } dictionary && Scripts.Dominant(word) is { } script &&
               dictionary.Knows(script) && dictionary.AcceptsSpelling(word, script);
    }

    public bool Knows(string word)
    {
        return adviser is { IsReady: true } dictionary && Scripts.Dominant(word) is { } script &&
               dictionary.Knows(script) && dictionary.KnowsWord(word, script);
    }
    public static bool HasLetter(string text)
    {
        return text.Any(char.IsLetter);
    }

    // The keyboard the word was typed on: the active one, or an installed one of the word's own script.
    private static KeyboardMap? MapFor(
        string word, LayoutSource active, IReadOnlyList<LayoutSource> installed, IReadOnlyList<LayoutCandidate> candidates)
    {
        var script = Scripts.Dominant(word);
        if (script is null || script == active.Script)
            return active.Map;

        foreach (var layout in installed)
            if (layout.Script == script)
                return layout.Map;

        foreach (var candidate in candidates)
            if (candidate.ScoringScript == script)
                return candidate.Map;

        return null;
    }

    private static (string Lead, string Core, string Trail) Split(string word)
    {
        var start = 0;
        var end = word.Length;
        while (start < end && !char.IsLetter(word[start])) start++;
        while (end > start && !char.IsLetter(word[end - 1])) end--;
        return (word[..start], word[start..end], word[end..]);
    }
}
