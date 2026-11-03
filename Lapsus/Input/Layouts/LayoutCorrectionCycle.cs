using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lapsus.Input;

internal sealed class LayoutCorrectionCycle
{
    // As PhraseEmitter settles a line: shorter words ("ye", "по") are too easily both languages'.
    private const int MinKnownWordLetters = 3;

    private readonly List<LayoutCycleOption> _options;

    private LayoutCorrectionCycle(KeyboardLayout? originalLayout, List<LayoutCycleOption> options)
    {
        OriginalLayout = originalLayout;
        _options = options;
        Index = 0;
    }

    public KeyboardLayout? OriginalLayout { get; }

    public int Index { get; private set; }

    public LayoutCycleOption Current => _options[Index];

    public bool MatchesCurrentText(string text)
    {
        return string.Equals(text, Current.Text, StringComparison.Ordinal);
    }

    public LayoutCycleOption MoveNext()
    {
        Index = (Index + 1) % _options.Count;
        return _options[Index];
    }

    public static LayoutCorrectionCycle? Start(
        IPhraseCorrector corrector,
        string segment,
        InstalledLayout active,
        IReadOnlyList<InstalledLayout> installed,
        KeyboardLayout? preferred)
    {
        var candidates = InstalledLayoutCorrection.BuildCandidates(installed);
        if (candidates.Count == 0)
            return null;

        var activeSource = new LayoutSource(active.Script, active.LanguageCode, active.Map, active.LayoutId);
        var sources = installed
            .Select(l => new LayoutSource(l.Script, l.LanguageCode, l.Map, l.LayoutId))
            .ToArray();

        var first = corrector.CorrectPhrase(segment, activeSource, sources, candidates, preferred);
        return FromReading(segment, first, corrector, active, installed, candidates, "");
    }

    public static LayoutCorrectionCycle? StartAuto(
        string chunk,
        PhraseCorrection first,
        InstalledLayout active,
        IReadOnlyList<InstalledLayout> installed,
        IPhraseCorrector corrector)
    {
        if (!first.Changed)
            return null;

        var candidates = InstalledLayoutCorrection.BuildCandidates(installed);
        return FromReading(chunk, first, corrector, active, installed, candidates, " ");
    }

    public static LayoutCorrectionCycle? StartFromAnswer(
        string segment,
        PhraseCorrection first,
        InstalledLayout active,
        IReadOnlyList<InstalledLayout> installed,
        IPhraseCorrector corrector)
    {
        if (!first.Changed)
            return null;

        var candidates = InstalledLayoutCorrection.BuildCandidates(installed);
        return FromReading(segment, first, corrector, active, installed, candidates, "");
    }

    private static LayoutCorrectionCycle? FromReading(
        string segment,
        PhraseCorrection first,
        IPhraseCorrector corrector,
        InstalledLayout active,
        IReadOnlyList<InstalledLayout> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        string suffix)
    {
        var options = new List<LayoutCycleOption>();

        if (corrector.SupportsLayoutCycle)
        {
            var sourceMaps = ResolveSourceMaps(segment, active, installed);
            var keep = SettledSpans(first, segment, corrector);

            if (first.Changed)
                AddOption(options, BrainOption(first, active, installed));

            foreach (var candidate in candidates)
            {
                if (candidate.LayoutId == active.LayoutId)
                    continue;

                if (CreateTranscodeOption(segment, sourceMaps, candidate, keep, options) is { } option)
                    options.Add(option);
            }
        }
        else if (first.Changed)
        {
            AddOption(options, BrainOption(first, active, installed));
        }

        if (options.Count == 0)
            return null;

        AddOption(options, new LayoutCycleOption(segment, active.Target, active.LayoutId));

        if (suffix.Length > 0)
            for (var i = 0; i < options.Count; i++)
                options[i] = options[i] with { Text = options[i].Text + suffix };

        return new LayoutCorrectionCycle(active.Target, options);
    }

    private static List<KeyboardMap> ResolveSourceMaps(
        string segment,
        InstalledLayout active,
        IReadOnlyList<InstalledLayout> installed)
    {
        var script = Scripts.Dominant(segment) ?? active.Script;
        var primary = script is null || active.Script == script
            ? active.Map
            : installed.FirstOrDefault(l => l.Script == script)?.Map ?? active.Map;

        var maps = new List<KeyboardMap> { primary };
        if (!maps.Contains(active.Map))
            maps.Add(active.Map);

        foreach (var layout in installed)
            if (!maps.Contains(layout.Map))
                maps.Add(layout.Map);

        return maps;
    }

    private static LayoutCycleOption BrainOption(
        PhraseCorrection first, InstalledLayout active, IReadOnlyList<InstalledLayout> installed)
    {
        var layout = first.TargetLayout;
        var id = first.TargetLayoutId;
        if (layout is null && string.IsNullOrEmpty(id))
            return new LayoutCycleOption(first.Corrected, active.Target, active.LayoutId);

        if (string.IsNullOrEmpty(id) && layout is not null)
            id = installed.FirstOrDefault(l => l.Target == layout)?.LayoutId
                 ?? installed.FirstOrDefault(l =>
                     LayoutLanguage.FromKeyboardLayout(layout.Value) is { } code
                     && l.LanguageCode == code)?.LayoutId;

        return new LayoutCycleOption(first.Corrected, layout, id);
    }

    private static IReadOnlyList<TextSpan>? SettledSpans(
        PhraseCorrection first, string segment, IPhraseCorrector corrector)
    {
        if (!string.Equals(first.Original, segment, StringComparison.Ordinal))
            return null;

        IReadOnlyList<TextSpan> spans = [.. first.Settled ?? [], .. KnownWords(segment, corrector)];

        // Words the brain read best as typed ("великолепный" a short list lacks) are spared while something else
        // is left to flip ("ГЧ"); with nothing else, they are what the press flips.
        if (first.Kept is { Count: > 0 } kept &&
            !TextSpans.CoversEveryLetter(segment, [.. spans, .. kept]))
            return [.. spans, .. kept];

        if (spans.Count == 0)
            return null;

        if (!TextSpans.CoversEveryLetter(segment, spans))
            return spans;

        var absolute = spans.Where(s => s.Absolute).ToArray();
        return absolute.Length == 0 ? null : absolute;
    }

    // Whatever the brain reported, a word the dictionary knows as typed ("пройдись") is never flipped into
    // another layout's gibberish; the whole line still flips once every word is known.
    private static List<TextSpan> KnownWords(string segment, IPhraseCorrector corrector)
    {
        var spans = new List<TextSpan>();
        var shortWords = new List<(TextSpan Span, Script Script)>();
        var settledScripts = new HashSet<Script>();
        for (var i = 0; i < segment.Length;)
        {
            if (char.IsWhiteSpace(segment[i]))
            {
                i++;
                continue;
            }

            var end = i;
            while (end < segment.Length && !char.IsWhiteSpace(segment[end]))
                end++;

            var start = i;
            var stop = end;
            while (start < stop && !char.IsLetter(segment[start])) start++;
            while (stop > start && !char.IsLetter(segment[stop - 1])) stop--;

            var word = segment[start..stop];
            if (Scripts.Dominant(word) is { } script && Scripts.IsWordOf(word, script) &&
                corrector.Knows(script) && corrector.AcceptsSpelling(word, script))
            {
                if (word.Length >= MinKnownWordLetters)
                {
                    spans.Add(new TextSpan(start, stop - start));
                    settledScripts.Add(script);
                }
                else
                    shortWords.Add((new TextSpan(start, stop - start), script));
            }

            i = end;
        }

        // "и", "по" alone could be keys meant for "b", "gj"; beside a known word of their script they are not.
        foreach (var (span, script) in shortWords)
            if (settledScripts.Contains(script))
                spans.Add(span);

        return spans;
    }

    private static LayoutCycleOption? CreateTranscodeOption(
        string segment,
        IReadOnlyList<KeyboardMap> sourceMaps,
        LayoutCandidate candidate,
        IReadOnlyList<TextSpan>? keep,
        IReadOnlyList<LayoutCycleOption> offered)
    {
        var text = keep is null
            ? LayoutTranscoder.TranscodeIntoScript(
                segment, sourceMaps, candidate.Map, candidate.ScoringScript)
            : LayoutTranscoder.TranscodeIntoScript(
                segment, sourceMaps, candidate.Map, candidate.ScoringScript, keep);

        if (string.Equals(text, segment, StringComparison.Ordinal)
            || offered.Any(o => string.Equals(o.Text, text, StringComparison.Ordinal)))
            return null;

        return new LayoutCycleOption(text, candidate.Target, candidate.LayoutId);
    }

    private static void AddOption(List<LayoutCycleOption> options, LayoutCycleOption option)
    {
        if (options.Any(o => o.TargetLayoutId == option.TargetLayoutId && o.Text == option.Text))
            return;

        options.Add(option);
    }
}

internal readonly record struct LayoutCycleOption(
    string Text,
    KeyboardLayout? TargetLayout,
    string? TargetLayoutId);
