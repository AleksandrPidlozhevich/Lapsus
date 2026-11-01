using Lapsus.Core.Layout;
using System.Text;

namespace Lapsus.Core.Correction;

internal static class PhraseEmitter
{
    // Short words must not settle a line: "d"/"vs" are also how "в"/"мы" get typed.
    private const int MinWordToSettle = 3;

    private static IReadOnlyList<TokenReading> RetypeGluedPunctuation(
        string text, IReadOnlyList<TokenReading> readings, IReadOnlyList<TextSpan>? excluded)
    {
        List<TokenReading>? retyped = null;
        bool[]? excludedMask = null;
        var at = 0;

        for (var i = 0; i < readings.Count; i++)
        {
            var start = at;
            at += readings[i].Typed.Length;

            if (i == 0 || readings[i].IsWord || SwitchedMaps(readings[i - 1]) is not { } switched)
                continue;

            var punctuation = readings[i].Typed;
            var run = 0;
            while (run < punctuation.Length && !char.IsWhiteSpace(punctuation[run]))
                run++;

            if (run == punctuation.Length && i + 1 < readings.Count &&
                SwitchedMaps(readings[i + 1])?.Into != switched.Into)
                continue;

            var (from, to, _) = switched;

            if (excluded is not null)
                excludedMask ??= TextSpans.BuildMask(text.Length, excluded);

            var chars = punctuation.ToCharArray();
            for (var k = 0; k < run; k++)
            {
                if (excludedMask is not null && excludedMask[start + k])
                    continue;

                if (!from.TryGetKey(chars[k], out var slot, out var shift))
                    continue;

                var mapped = to.CharAtSlot(slot, shift);
                if (mapped != '\0' && !char.IsLetter(mapped))
                    chars[k] = mapped;
            }

            var result = new string(chars);
            if (string.Equals(result, punctuation, StringComparison.Ordinal))
                continue;

            retyped ??= [.. readings];
            retyped[i] = readings[i] with { Chosen = ScoredWord.AsTyped(result, 0.0) };
        }

        return retyped ?? readings;
    }

    private static (KeyboardMap From, KeyboardMap To, Script Into)? SwitchedMaps(in TokenReading token)
    {
        if (!token.IsWord || !token.Chosen.Changed || token.ChosenScript is not { } into ||
            into == token.Source.Script)
            return null;

        foreach (var candidate in token.Source.Candidates)
        {
            if (candidate.ScoringScript != into)
                continue;

            var sameLayout = !string.IsNullOrEmpty(token.Chosen.TargetLayoutId)
                ? candidate.LayoutId == token.Chosen.TargetLayoutId
                : candidate.Target == token.Chosen.Target;
            if (sameLayout)
                return (token.Source.Map, candidate.Map, into);
        }

        return null;
    }
    public static PhraseCorrection Emit(
        WordScorer scorer, string text, IReadOnlyList<TokenReading> readings, KeyboardLayout? preferred,
        IReadOnlyList<TextSpan>? excluded)
    {
        readings = RetypeGluedPunctuation(text, readings, excluded);
        var sb = new StringBuilder(text.Length);
        var votes = new TargetVotes();

        List<TextSpan>? settled = excluded is null ? null : [.. excluded];
        List<TextSpan>? kept = null;
        List<(TextSpan Span, Script? Script)>? shortListed = null;
        HashSet<Script?>? settledScripts = null;
        var at = 0;

        foreach (var token in readings)
        {
            sb.Append(token.Chosen.Text);

            var start = at;
            at += token.Typed.Length;

            if (!token.IsWord)
                continue;

            var listedAsTyped = !token.Chosen.Changed && WordScorer.IsDictionaryHit(token.Baseline);
            if (token.IsException)
                (settled ??= []).Add(new TextSpan(start, token.Typed.Length, true));
            else if (listedAsTyped &&
                     WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length >= MinWordToSettle)
            {
                // Advisory only while part of the line still needs work; drop once every letter is settled.
                (settled ??= []).Add(new TextSpan(start, token.Typed.Length));
                (settledScripts ??= []).Add(token.Source.Script);
            }
            else if (!token.Chosen.Changed && ReadsBestAsTyped(scorer, token))
                (kept ??= []).Add(new TextSpan(start, token.Typed.Length));
            else if (listedAsTyped)
                (shortListed ??= []).Add((new TextSpan(start, token.Typed.Length), token.Source.Script));

            if (token.Chosen.Changed)
            {
                if (token.Chosen.Target is not null || !string.IsNullOrEmpty(token.Chosen.TargetLayoutId))
                    votes.Add(token.Chosen.Target, token.Chosen.TargetLayoutId);

                continue;
            }

            if (!WordScorer.IsDictionaryHit(token.Baseline))
                continue;

            var (own, ownId) = OwnLayoutOf(token.Source);
            if (own is not null || !string.IsNullOrEmpty(ownId))
                votes.Add(own, ownId, counter: true);
        }

        // "и", "в" alone could be keys meant for "b", "d"; beside a settled word of their script they are not.
        if (shortListed is not null && settledScripts is not null)
            foreach (var (span, script) in shortListed)
                if (settledScripts.Contains(script))
                    settled!.Add(span);

        var corrected = sb.ToString();
        var (targetLayout, targetLayoutId) = votes.Pick(preferred);

        // If contiguity breaks, report no spans rather than wrong ones.
        if (at != text.Length)
        {
            settled = null;
            kept = null;
        }

        return new PhraseCorrection(
            text, corrected, !string.Equals(corrected, text, StringComparison.Ordinal),
            targetLayout, targetLayoutId, settled, kept);
    }

    private static bool ReadsBestAsTyped(WordScorer scorer, in TokenReading token)
    {
        foreach (var candidate in token.Source.Candidates)
        {
            if (candidate.ScoringScript == token.Source.Script)
                continue;

            var switched = LayoutTranscoder.Transcode(token.Typed, token.Source.Map, candidate.Map);
            if (switched == token.Typed || !WordScanner.IsAllLetters(switched, candidate.ScoringScript))
                continue;

            var score = scorer.Score(switched, candidate.ScoringScript, candidate.LanguageCode);
            if (WordScorer.IsDictionaryHit(score) || WordScorer.IsWeakDictionaryHit(score) || score > token.Baseline)
                return false;
        }

        return true;
    }

    private static (KeyboardLayout? Layout, string? LayoutId) OwnLayoutOf(WordSource source)
    {
        foreach (var candidate in source.Candidates)
            if (candidate.ScoringScript == source.Script && ReferenceEquals(candidate.Map, source.Map))
                return (candidate.Target, candidate.LayoutId);

        foreach (var candidate in source.Candidates)
            if (candidate.ScoringScript == source.Script)
                return (candidate.Target, candidate.LayoutId);

        return (null, null);
    }
}
