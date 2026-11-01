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

    // Per word in a script the rest of the line is not written in.
    internal const double MixedScriptPenalty = 4.0;

    // A longer text is ranked a window at a time, cut after a sentence where one ends in reach: the model
    // reads a window as one line, and a paragraph in one piece would cost a long prompt per doubtful word.
    private const int MaxWords = 24;

    private static readonly char[] SentenceEnds = ['.', '!', '?', '…'];

    // A word of this many letters the dictionary knows is settled without asking the model.
    private const int MinLettersToSettle = 4;

    private readonly WordReadingsBuilder _readings = new(adviser, spellingLanguages);

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
        var chunks = PhraseChunks.Of(text);
        if (chunks.Count == 0)
            return null;

        if (chunks.Count <= MaxWords)
            return RankWindow(text, active, installed, candidates, advice, cancellationToken);

        var advised = PhraseChunks.Words(advice, chunks.Count);
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
                : PhraseChunks.Replace(window, PhraseChunks.Of(window), advised[from..to]);

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
        var chunks = PhraseChunks.Of(text);

        var typed = chunks.Select(c => text.Substring(c.Start, c.Length)).ToArray();
        var advised = PhraseChunks.Words(advice, typed.Length);
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
                ? new WordReadings([new Reading(typed[i], WordReadingsBuilder.KeepBias)], new string?[candidates.Count])
                : _readings.Build(typed[i], advised?[i], active, installed, candidates, switchedAny);
        }
        for (var i = 0; i < typed.Length; i++)
            doubtful[i] = readings[i].All.Count > 1 &&
                          !Settled(current[i], typed[i], switchedAny ? null : LineScript(current, i));

        if (!doubtful.Contains(true))
            return new Ranked(PhraseChunks.Replace(text, chunks, current), false);

        // A line the dictionary kept and knows every word of is not the model's to overrule: it reads
        // "ну ні є" as less likely than "ye ys є".
        if (advice is null && typed.All(word => !WordReadingsBuilder.HasLetter(word) || _readings.Knows(word)))
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
            var lines = starts.Select(words => PhraseChunks.Join(text, chunks, words, 0, words.Length)).ToList();
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
            var head = PhraseChunks.Join(text, chunks, current, 0, i);
            var last = readings[i].Spellings ? typed.Length - 1 : Math.Min(i + 1, typed.Length - 1);
            // Separators are whitespace, so trimming drops the one Join leaves after the last word.
            var tail = last > i
                ? PhraseChunks.Separator(text, chunks, i) + PhraseChunks.Join(text, chunks, current, i + 1, last + 1).TrimEnd()
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

        return new Ranked(PhraseChunks.Replace(text, chunks, current), true);
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

    // A long word the dictionary kept, or switched layout into, is settled; one it spell-fixed in place is
    // left for the model to choose among the other spellings.
    // A known word kept in the script its neighbours are written in is settled at any length: the model
    // pays for Georgian or Ukrainian in many tokens and would trade "ორ" for "or".
    private bool Settled(string word, string typed, Script? line)
    {
        if (word != typed && Scripts.Dominant(word) == Scripts.Dominant(typed))
            return false;

        if (Scripts.Dominant(word) is not { } script || !_readings.Knows(word))
            return false;

        return (word == typed && script == line) ||
               WordScanner.TrimToLetters(word, script).Length >= MinLettersToSettle;
    }
}
