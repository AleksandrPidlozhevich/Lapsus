using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public sealed class PlainLayoutSwitcher(WordExceptions? exceptions = null) : IPhraseCorrector
{
    private readonly WordExceptions _exceptions = exceptions ?? new WordExceptions();

    public bool IsReady => true;

    public bool SupportsLayoutCycle => true;

    public bool PreferAsync => false;

    public PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred = null)
    {
        // Carry keep on every return; otherwise the hotkey circle re-encodes declined lines.
        var keep = _exceptions.IsEmpty ? null : _exceptions.ChunkSpans(text);

        if (string.IsNullOrEmpty(text) || candidates.Count == 0)
            return new PhraseCorrection(text, text, false, null, null, keep);

        var script = Majority(text) ?? active.Script;
        var sourceMap = SourceMap(script, active, installed);
        var sourceMaps = SourceMaps(sourceMap, active, installed);
        var mixed = Scripts.IsMixed(text);

        foreach (var candidate in Ordered(candidates, preferred, script, text, sourceMap, mixed))
        {
            var transcoded = keep is null
                ? LayoutTranscoder.TranscodeIntoScript(
                    text, sourceMaps, candidate.Map, candidate.ScoringScript)
                : LayoutTranscoder.TranscodeIntoScript(
                    text, sourceMaps, candidate.Map, candidate.ScoringScript, keep);

            if (string.Equals(transcoded, text, StringComparison.Ordinal))
                continue;

            return new PhraseCorrection(
                text, transcoded, true, candidate.Target, candidate.LayoutId, keep);
        }

        return new PhraseCorrection(text, text, false, null, null, keep);
    }

    private static List<LayoutCandidate> Ordered(
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred,
        Script? script,
        string text,
        KeyboardMap sourceMap,
        bool mixed)
    {
        var ordered = new List<LayoutCandidate>(candidates.Count);

        void Offer(LayoutCandidate candidate)
        {
            if (!ordered.Contains(candidate))
                ordered.Add(candidate);
        }

        bool IsPreferred(LayoutCandidate candidate)
        {
            // Null preferred must not match unresolved Target.
            return preferred is not null && candidate.Target == preferred;
        }

        if (mixed && script is not null)
        {
            foreach (var candidate in candidates)
                if (candidate.ScoringScript == script && IsPreferred(candidate))
                    Offer(candidate);

            foreach (var candidate in candidates)
                if (candidate.ScoringScript == script)
                    Offer(candidate);
        }

        if (preferred is not null)
            foreach (var candidate in candidates)
                if (candidate.ScoringScript != script && IsPreferred(candidate))
                    Offer(candidate);

        foreach (var candidate in candidates)
            if (candidate.ScoringScript != script && !IsPreferred(candidate))
                Offer(candidate);

        if (script is not { } typedScript)
            return ordered;

        foreach (var candidate in candidates)
            if (candidate.ScoringScript == typedScript &&
                LayoutTranscoder.HasMistypedLetterKey(text, typedScript, sourceMap, candidate.Map))
                Offer(candidate);

        return ordered;
    }

    private static Script? Majority(string text)
    {
        var counts = new Dictionary<Script, int>();
        var order = new List<Script>();
        foreach (var ch in text)
        {
            if (Scripts.Of(ch) is not { } script)
                continue;

            if (counts.TryGetValue(script, out var seen))
            {
                counts[script] = seen + 1;
                continue;
            }

            counts[script] = 1;
            order.Add(script);
        }

        Script? best = null;
        var bestCount = 0;
        foreach (var script in order)
        {
            if (counts[script] <= bestCount)
                continue;

            best = script;
            bestCount = counts[script];
        }

        return best;
    }

    private static KeyboardMap SourceMap(
        Script? script, LayoutSource active, IReadOnlyList<LayoutSource> installed)
    {
        if (script is null || active.Script == script)
            return active.Map;

        foreach (var source in installed)
            if (source.Script == script)
                return source.Map;

        return active.Map;
    }

    private static List<KeyboardMap> SourceMaps(
        KeyboardMap primary, LayoutSource active, IReadOnlyList<LayoutSource> installed)
    {
        var maps = new List<KeyboardMap> { primary };

        if (!maps.Contains(active.Map))
            maps.Add(active.Map);

        foreach (var source in installed)
            if (!maps.Contains(source.Map))
                maps.Add(source.Map);

        return maps;
    }
}
