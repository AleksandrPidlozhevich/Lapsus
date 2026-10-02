using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

// The neural brain's main path. Every word gets the readings the dictionary brain weighs — as typed, the
// dictionary's pick, through each layout, spell-fixed — and the model, reading them in context as plain
// text, picks one. A small model cannot be trusted to write the answer ("djpnvb lfyyst bp api" comes back
// as "текст"), but it reliably tells "возьми данные из api" from "возьми данные bp api".
internal sealed class ReadingRanker(
    ILocalLlm llm, IPhraseCorrector? adviser, WordExceptions? exceptions, IReadOnlySet<string>? spellingLanguages = null)
{
    // Plain text after a line break: nothing for the model to misread as an instruction.
    private const string Context = "\n";

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

    // Per word in a script the rest of the line is not written in.
    internal const double MixedScriptPenalty = 4.0;

    // A longer text is ranked a window at a time, cut after a sentence where one ends in reach: the model
    // reads a window as one line, and a paragraph in one piece would cost a long prompt per doubtful word.
    private const int MaxWords = 24;

    private static readonly char[] SentenceEnds = ['.', '!', '?', '…'];

    private const int MaxReadingsPerWord = 10;

    // Of the dictionary's spellings for a slip, the ones a finger most likely produced it from.
    private const int MaxSpellingsPerWord = 5;

    // A word of this many letters the dictionary knows is settled without asking the model.
    private const int MinLettersToSettle = 4;

    private static readonly char[] GluedPunctuation = ['.', ',', '!', '?', ';', ':'];

    private readonly record struct Reading(string Text, double Prior);

    // Every reading of one word, which of them each candidate layout gives, and whether they are only
    // spellings of a slip (then the whole rest of the line is context worth reading).
    private sealed record WordReadings(
        List<Reading> All, string?[] ByLayout, bool Spellings = false, string?[]? ByLigature = null)
    {
        public double PriorOf(string text)
        {
            var at = All.FindIndex(r => r.Text == text);
            return at < 0 ? 0.0 : All[at].Prior;
        }
    }

    public readonly record struct Ranked(string Text, bool AskedModel);

    // Null when there is nothing to rank or the model cannot score.
    public Ranked? Rank(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        string? advice,
        CancellationToken cancellationToken = default)
    {
        var chunks = NeuralPhraseRewriter.Chunks(text);
        if (chunks.Count == 0)
            return null;

        if (chunks.Count <= MaxWords)
            return RankWindow(text, active, installed, candidates, advice, cancellationToken);

        var advised = Advised(advice, chunks.Count);
        var sb = new System.Text.StringBuilder(text.Length + 16);
        var at = 0;
        var asked = false;
        foreach (var (from, to) in Windows(text, chunks))
        {
            var start = chunks[from].Start;
            var end = chunks[to - 1].Start + chunks[to - 1].Length;
            var window = text[start..end];
            var windowAdvice = advised is null
                ? null
                : Replace(window, NeuralPhraseRewriter.Chunks(window), advised[from..to]);

            if (RankWindow(window, active, installed, candidates, windowAdvice, cancellationToken) is not { } ranked)
                return null;

            sb.Append(text, at, start - at).Append(ranked.Text);
            at = end;
            asked |= ranked.AskedModel;
        }

        return new Ranked(sb.Append(text, at, text.Length - at).ToString(), asked);
    }

    // Chunk ranges of at most MaxWords, each ending after the last sentence end it reaches if it has one.
    private static IEnumerable<(int From, int To)> Windows(string text, List<(int Start, int Length)> chunks)
    {
        var from = 0;
        while (from < chunks.Count)
        {
            var to = Math.Min(from + MaxWords, chunks.Count);
            if (to < chunks.Count)
                for (var i = to - 1; i > from; i--)
                    if (Array.IndexOf(SentenceEnds, text[chunks[i].Start + chunks[i].Length - 1]) >= 0)
                    {
                        to = i + 1;
                        break;
                    }

            yield return (from, to);
            from = to;
        }
    }

    private Ranked? RankWindow(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        string? advice,
        CancellationToken cancellationToken)
    {
        var chunks = NeuralPhraseRewriter.Chunks(text);

        var typed = chunks.Select(c => text.Substring(c.Start, c.Length)).ToArray();
        var advised = Advised(advice, typed.Length);
        var listed = exceptions?.ChunkSpans(text);

        // Once the dictionary moved a word to another script the line is on the wrong layout: a known word
        // agreeing with its neighbours ("ის" among "ანდ", "ნიგჰტს") proves nothing, and a word it spell-fixed
        // in place ("لاشؤن" → "لان" among English) is more likely a remap it could not see.
        var switchedAny = advised is not null &&
                          typed.Where((word, i) => Scripts.Dominant(advised[i]) != Scripts.Dominant(word)).Any();

        var current = new string[typed.Length];
        var readings = new WordReadings[typed.Length];
        var doubtful = new bool[typed.Length];
        for (var i = 0; i < typed.Length; i++)
        {
            current[i] = advised?[i] ?? typed[i];
            var isListed = listed?.Exists(span => span.Start == chunks[i].Start) ?? false;
            readings[i] = isListed
                ? new WordReadings([new Reading(typed[i], KeepBias)], new string?[candidates.Count])
                : Readings(typed[i], advised?[i], active, installed, candidates, switchedAny);
        }
        for (var i = 0; i < typed.Length; i++)
            doubtful[i] = readings[i].All.Count > 1 &&
                          !Settled(current[i], typed[i], switchedAny ? null : LineScript(current, i));

        if (!doubtful.Contains(true))
            return new Ranked(Replace(text, chunks, current), false);

        // A line the dictionary kept and knows every word of is not the model's to overrule: it reads
        // "ну ні є" as less likely than "ye ys є".
        if (advice is null && typed.All(word => !HasLetter(word) || Knows(word)))
            return new Ranked(text, false);

        // Word by word, a line typed wholly on the wrong layout has only wrong neighbours to lean on;
        // weigh it switched as a whole first.
        var starts = new List<string[]> { current };
        for (var c = 0; c < candidates.Count; c++)
            foreach (var asKeys in new[] { false, true })
            {
                var switched = (string[])current.Clone();
                for (var i = 0; i < typed.Length; i++)
                {
                    var remap = (asKeys ? readings[i].ByLigature?[c] : null) ?? readings[i].ByLayout[c];
                    if (doubtful[i] && remap is not null)
                        switched[i] = remap;
                }

                if (!starts.Exists(s => s.SequenceEqual(switched)))
                    starts.Add(switched);
            }

        if (starts.Count > 1)
        {
            var lines = starts.Select(words => Join(text, chunks, words, 0, words.Length)).ToList();
            if (Scores(lines, ends: true, cancellationToken) is not { } lineScores)
                return null;

            var best = 0;
            for (var s = 1; s < starts.Count; s++)
                if (lineScores[s] + PriorOf(starts[s], readings) > lineScores[best] + PriorOf(starts[best], readings))
                    best = s;

            current = starts[best];
        }

        for (var i = 0; i < typed.Length; i++)
        {
            if (!doubtful[i])
                continue;

            // Left context as chosen so far, the word, and one word of look-ahead — or, for a slip, all
            // that follows: which spelling was meant often shows only further on.
            var head = Join(text, chunks, current, 0, i);
            var last = readings[i].Spellings ? typed.Length - 1 : Math.Min(i + 1, typed.Length - 1);
            // Separators are whitespace, so trimming drops the one Join leaves after the last word.
            var tail = last > i
                ? Separator(text, chunks, i) + Join(text, chunks, current, i + 1, last + 1).TrimEnd()
                : string.Empty;
            var options = readings[i].All;
            var texts = options.Select(r => head + r.Text + tail).ToList();
            var line = LineScript(current, i);

            if (Scores(texts, ends: last >= typed.Length - 1, cancellationToken) is not { } scores)
                return null;

            double Total(int r) => scores[r] + options[r].Prior + MixPenalty(options[r].Text, line);

            var best = 0;
            for (var r = 1; r < texts.Count; r++)
                if (Total(r) > Total(best))
                    best = r;

            current[i] = options[best].Text;
        }

        return new Ranked(Replace(text, chunks, current), true);
    }

    private IReadOnlyList<double>? Scores(List<string> texts, bool ends, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scores = llm.ScoreAsync(Context, texts, ends, cancellationToken).GetAwaiter().GetResult();
        return scores is not null && scores.Count == texts.Count ? scores : null;
    }

    private static double PriorOf(string[] words, WordReadings[] readings)
    {
        var prior = 0.0;
        for (var i = 0; i < words.Length; i++)
            prior += readings[i].PriorOf(words[i]) + MixPenalty(words[i], LineScript(words, i));

        return prior;
    }

    private static string[]? Advised(string? advice, int count)
    {
        if (advice is null)
            return null;

        var chunks = NeuralPhraseRewriter.Chunks(advice);
        return chunks.Count == count ? chunks.Select(c => advice.Substring(c.Start, c.Length)).ToArray() : null;
    }

    private WordReadings Readings(
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

    // The script most of the other words are in, if most of them share one.
    private static Script? LineScript(string[] words, int except)
    {
        var counts = new Dictionary<Script, int>();
        var total = 0;
        for (var i = 0; i < words.Length; i++)
        {
            if (i == except || Scripts.Dominant(words[i]) is not { } script)
                continue;

            counts[script] = counts.GetValueOrDefault(script) + 1;
            total++;
        }

        foreach (var (script, count) in counts)
            if (count * 2 > total)
                return script;

        return null;
    }

    private static double MixPenalty(string word, Script? line)
    {
        return line is { } script && Scripts.Dominant(word) is { } own && own != script ? -MixedScriptPenalty : 0.0;
    }

    private bool Accepts(string word)
    {
        return adviser is { IsReady: true } dictionary && Scripts.Dominant(word) is { } script &&
               dictionary.Knows(script) && dictionary.AcceptsSpelling(word, script);
    }

    private bool Knows(string word)
    {
        return adviser is { IsReady: true } dictionary && Scripts.Dominant(word) is { } script &&
               dictionary.Knows(script) && dictionary.KnowsWord(word, script);
    }

    // A long word the dictionary kept, or switched layout into, is settled; one it spell-fixed in place is
    // left for the model to choose among the other spellings.
    // A known word kept in the script its neighbours are written in is settled at any length: the model
    // pays for Georgian or Ukrainian in many tokens and would trade "ორ" for "or".
    private bool Settled(string word, string typed, Script? line)
    {
        if (word != typed && Scripts.Dominant(word) == Scripts.Dominant(typed))
            return false;

        if (Scripts.Dominant(word) is not { } script || !Knows(word))
            return false;

        return (word == typed && script == line) ||
               WordScanner.TrimToLetters(word, script).Length >= MinLettersToSettle;
    }

    private static bool HasLetter(string text)
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

    private static string Separator(string text, List<(int Start, int Length)> chunks, int i)
    {
        var from = chunks[i].Start + chunks[i].Length;
        return text[from..chunks[i + 1].Start];
    }

    private static string Join(string text, List<(int Start, int Length)> chunks, string[] words, int from, int to)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = from; i < to; i++)
        {
            sb.Append(words[i]);
            if (i + 1 < chunks.Count)
                sb.Append(Separator(text, chunks, i));
        }

        return sb.ToString();
    }

    private static string Replace(string text, List<(int Start, int Length)> chunks, string[] words)
    {
        var sb = new System.Text.StringBuilder(text.Length + 8);
        var at = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            sb.Append(text, at, chunks[i].Start - at).Append(words[i]);
            at = chunks[i].Start + chunks[i].Length;
        }

        return sb.Append(text, at, text.Length - at).ToString();
    }
}
