using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

// Which keyboard a word was typed on and which layouts may read it.
internal static class WordSources
{
    public static int? LetterlessChunkEnd(
        string text, int start, Func<char, WordSource> sourceFor, IReadOnlyList<LayoutSource>? installed,
        bool[]? excludedMask)
    {
        var end = start;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            if (char.IsLetter(text[end]) || (excludedMask is not null && excludedMask[end]))
                return null;

            end++;
        }

        var raw = KeysSource(text[start..end], sourceFor, installed);
        var candidates = EnsureOppositeScriptCandidates(raw.Script, raw.Candidates, installed);
        for (var k = start; k < end; k++)
            if (!WordScanner.CarriesLetterElsewhere(text[k], raw.Map, candidates))
                return null;

        return end;
    }

    // Letterless keys take the active layout's script, but "'[" cannot be typed on the Russian layout at all:
    // the layout that types them is the one they were typed on.
    public static WordSource KeysSource(
        string chunk, Func<char, WordSource> sourceFor, IReadOnlyList<LayoutSource>? installed)
    {
        var raw = sourceFor(chunk[0]);
        if (installed is null || Types(raw.Map, chunk))
            return raw;

        foreach (var layout in installed)
            if (layout.Script is { } script && Types(layout.Map, chunk))
                return raw with { Script = script, Map = layout.Map, LanguageCode = layout.LanguageCode };

        return raw;

        static bool Types(KeyboardMap map, string keys)
        {
            foreach (var ch in keys)
                if (!map.TryGetKey(ch, out _, out _))
                    return false;

            return true;
        }
    }
    public static string? LanguageOf(ScoredWord reading, WordSource source)
    {
        foreach (var candidate in source.Candidates)
        {
            var sameLayout = !string.IsNullOrEmpty(reading.TargetLayoutId)
                ? candidate.LayoutId == reading.TargetLayoutId
                : candidate.Target == reading.Target;
            if (sameLayout)
                return candidate.LanguageCode;
        }

        return null;
    }
    public static Script ScriptOf(ScoredWord best, WordSource source)
    {
        return Scripts.Dominant(best.Text) ?? source.Script;
    }
    public static LayoutCandidate? CandidateOf(ScoredWord reading, WordSource source)
    {
        foreach (var candidate in source.Candidates)
        {
            var sameLayout = !string.IsNullOrEmpty(reading.TargetLayoutId)
                ? candidate.LayoutId == reading.TargetLayoutId
                : candidate.Target == reading.Target;
            if (sameLayout)
                return candidate;
        }

        return null;
    }
    public static IReadOnlyList<LayoutCandidate> EnsureOppositeScriptCandidates(
        Script sourceScript, IReadOnlyList<LayoutCandidate> candidates, IReadOnlyList<LayoutSource>? installed)
    {
        foreach (var candidate in candidates)
            if (candidate.ScoringScript != sourceScript)
                return candidates;

        var withFallback = new List<LayoutCandidate>(candidates);
        withFallback.AddRange(OppositeScriptFallback(sourceScript, installed));
        return withFallback;
    }

    public static IReadOnlyList<LayoutCandidate> OppositeScriptFallback(
        Script sourceScript, IReadOnlyList<LayoutSource>? installed)
    {
        if (installed is not null)
        {
            var fromInstalled = new List<LayoutCandidate>();
            foreach (var layout in installed)
            {
                if (layout.Script is not { } script || script == sourceScript)
                    continue;

                fromInstalled.Add(new LayoutCandidate(
                    script,
                    LayoutLanguage.ToKeyboardLayout(layout.LanguageCode),
                    layout.Map,
                    layout.LanguageCode,
                    layout.LayoutId));
            }

            if (fromInstalled.Count > 0)
                return fromInstalled;
        }

        return ScriptLayouts.CandidatesFor(sourceScript);
    }
}
