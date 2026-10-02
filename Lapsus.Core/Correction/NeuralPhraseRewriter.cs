using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public sealed class NeuralPhraseRewriter : IPhraseCorrector
{
    private readonly ILocalLlm _llm;
    private readonly IPhraseCorrector? _adviser;
    private readonly WordExceptions? _exceptions;
    private readonly ReadingRanker _ranker;
    private readonly TimeSpan _baseBudget;

    // Past its time budget the model is cut off and the dictionary's answer goes out instead: a hotkey
    // that hangs is worse than one that does what the dictionary brain would. The budget grows with the
    // text, so a selected paragraph still gets read.
    private static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan BudgetPerWord = TimeSpan.FromMilliseconds(100);

    // spellingLanguages: where the model may choose among spellings of a slip (SpellingLanguages.For);
    // null for every language.
    public NeuralPhraseRewriter(
        ILocalLlm llm,
        IPhraseCorrector? adviser = null,
        WordExceptions? exceptions = null,
        TimeSpan? budget = null,
        IReadOnlySet<string>? spellingLanguages = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _adviser = adviser;
        _exceptions = exceptions;
        _ranker = new ReadingRanker(llm, adviser, exceptions, spellingLanguages);
        _baseBudget = budget ?? DefaultBudget;
    }

    public bool IsReady => _llm.IsLoaded || AdviserStandsIn;

    // The answer is built from per-layout readings, so pressing again walks the same circle the dictionary
    // brain offers — the line through each other layout, then the text as typed — without asking again.
    public bool SupportsLayoutCycle => _llm.IsLoaded || _adviser is { SupportsLayoutCycle: true };

    public bool PreferAsync => true;

    public bool Knows(Script script)
    {
        return _adviser?.Knows(script) ?? false;
    }

    public bool KnowsWord(string word, Script script)
    {
        return _adviser?.KnowsWord(word, script) ?? false;
    }

    private bool AdviserStandsIn => !_llm.IsLoaded && _adviser is { IsReady: true };

    public PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred = null)
    {
        return CorrectPhrase(text, active, installed, candidates, preferred, default);
    }

    public PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred,
        CorrectionHints hints)
    {
        if (string.IsNullOrEmpty(text))
            return new PhraseCorrection(text, text, false, null);

        if (AdviserStandsIn)
            return _adviser!.CorrectPhrase(text, active, installed, candidates, preferred, hints);

        if (!_llm.IsLoaded)
            return new PhraseCorrection(text, text, false, null);

        var remaps = NeuralRewritePrompt.CollectRemaps(text, active.Map, candidates, preferred);
        var advice = Advice(text, active, installed, candidates, preferred);
        var judgesEverything = JudgesEveryReading(active, candidates);

        var trusted = advice is { } candidate &&
                      (judgesEverything || candidate.Correction.TargetLayout is not null)
            ? advice
            : null;

        if (trusted is { } certain && IsOneWord(text) && judgesEverything)
            return certain.Correction;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(hints.Cancellation);
        budget.CancelAfter(_baseBudget + BudgetPerWord * Chunks(text).Count);
        try
        {
            return AskTheModel(text, active, installed, candidates, preferred, advice, trusted, remaps, budget.Token);
        }
        catch (OperationCanceledException) when (!hints.Cancellation.IsCancellationRequested)
        {
            return advice?.Correction ?? new PhraseCorrection(text, text, false, null);
        }
    }

    private PhraseCorrection AskTheModel(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred,
        (PhraseCorrection Correction, string Text)? advice,
        (PhraseCorrection Correction, string Text)? trusted,
        IReadOnlyList<string> remaps,
        CancellationToken cancellationToken)
    {
        var ranked = _ranker.Rank(text, active, installed, candidates, advice?.Correction.Corrected, cancellationToken);
        if (ranked is { } answered)
            return FromRanking(text, answered.Text, active, candidates, preferred, advice, trusted);

        var offered = Offer(advice, remaps);

        var system = NeuralRewritePrompt.BuildSystem(preferred);
        var user = NeuralRewritePrompt.BuildUser(text, active.Map, candidates, preferred, offered);
        var rewritten = _llm.CompleteAsync(system, user, text.Length, cancellationToken).GetAwaiter().GetResult();
        rewritten = SanitizeModelOutput(rewritten, text);

        Answer? answer;
        if (string.IsNullOrEmpty(rewritten) || IsEcho(rewritten, text))
            answer = WhenTheModelSaidNothing(text, trusted, remaps);
        else if (offered.Contains(rewritten, StringComparer.Ordinal))
            answer = Answer.FromModelOrRemap(rewritten);
        else
            answer = WhenNoCandidateHoldsTheAnswer(text, rewritten, trusted, remaps);

        if (answer is null)
            return new PhraseCorrection(text, text, false, null);

        var restored = RestoreOuterWhitespace(text, answer.Value.Text);
        if (answer.Value.Correction is { } dictated)
            return dictated;

        if (KeepListedWords(text, restored) is not { } kept)
            return trusted?.Correction ?? new PhraseCorrection(text, text, false, null);

        restored = kept;

        if (!WritesOnlyTypableScripts(text, restored, active, candidates) || !MovesOnlyIntoKnownWords(text, restored))
            return trusted?.Correction ?? new PhraseCorrection(text, text, false, null);

        if (string.Equals(restored, text, StringComparison.Ordinal))
            return new PhraseCorrection(text, text, false, null);

        var (target, targetId) = ResolveTarget(text, restored, active, candidates, preferred);
        return new PhraseCorrection(text, restored, true, target, targetId);
    }

    private PhraseCorrection FromRanking(
        string text,
        string ranked,
        LayoutSource active,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred,
        (PhraseCorrection Correction, string Text)? advice,
        (PhraseCorrection Correction, string Text)? trusted)
    {
        if (advice is { } dictionary && ranked == dictionary.Correction.Corrected)
            return dictionary.Correction;

        if (ranked == text)
            return new PhraseCorrection(text, text, false, null);

        if (!WritesOnlyTypableScripts(text, ranked, active, candidates) || !MovesOnlyIntoKnownWords(text, ranked))
            return trusted?.Correction ?? new PhraseCorrection(text, text, false, null);

        var (target, targetId) = ResolveTarget(text, ranked, active, candidates, preferred);
        return new PhraseCorrection(text, ranked, true, target, targetId);
    }

    // Listed chunks the model moved go back as typed; chunk-count mismatch → null.
    private string? KeepListedWords(string text, string rewritten)
    {
        if (_exceptions?.ChunkSpans(text) is not { } listed)
            return rewritten;

        var typed = Chunks(text);
        var answered = Chunks(rewritten);
        if (typed.Count != answered.Count)
            return null;

        var sb = new System.Text.StringBuilder(rewritten.Length);
        var at = 0;
        for (var i = 0; i < answered.Count; i++)
        {
            var (start, length) = answered[i];
            sb.Append(rewritten, at, start - at);

            var isListed = listed.Exists(span => span.Start == typed[i].Start);
            sb.Append(isListed ? text.AsSpan(typed[i].Start, typed[i].Length) : rewritten.AsSpan(start, length));
            at = start + length;
        }

        return sb.Append(rewritten, at, rewritten.Length - at).ToString();
    }

    // A letter in a script the user neither typed nor has a layout for is a translation, not a repair.
    private static bool WritesOnlyTypableScripts(
        string text, string rewritten, LayoutSource active, IReadOnlyList<LayoutCandidate> candidates)
    {
        foreach (var ch in rewritten)
        {
            if (Scripts.Of(ch) is not { } script || script == active.Script)
                continue;

            if (!candidates.Any(c => c.ScoringScript == script) && !text.Any(c => Scripts.Of(c) == script))
                return false;
        }

        return true;
    }

    // A word moved into a script the dictionary reads must be a word it knows; on a phonetic layout the
    // remap of correct English is a transliteration, and the model is happy to pick it.
    private bool MovesOnlyIntoKnownWords(string text, string rewritten)
    {
        if (_adviser is not { IsReady: true } adviser)
            return true;

        var typed = Chunks(text);
        var answered = Chunks(rewritten);
        var whole = Scripts.Dominant(text);
        for (var i = 0; i < answered.Count; i++)
        {
            var word = rewritten.Substring(answered[i].Start, answered[i].Length);
            var from = typed.Count == answered.Count
                ? Scripts.Dominant(text.Substring(typed[i].Start, typed[i].Length))
                : whole;

            if (Scripts.Dominant(word) is not { } into || into == from || !adviser.Knows(into))
                continue;

            if (!adviser.KnowsWord(word, into))
                return false;
        }

        return true;
    }

    internal static List<(int Start, int Length)> Chunks(string text)
    {
        var chunks = new List<(int Start, int Length)>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;

            if (i > start)
                chunks.Add((start, i - start));
        }

        return chunks;
    }

    private static Answer? WhenTheModelSaidNothing(
        string text, (PhraseCorrection Correction, string Text)? trusted, IReadOnlyList<string> remaps)
    {
        if (trusted is { } advice)
            return Answer.FromDictionary(advice.Correction);

        if (remaps.Count == 0 || !LooksLikeWrongLayoutRun(text))
            return null;

        return IsEcho(remaps[0], text) ? null : Answer.FromModelOrRemap(remaps[0]);
    }

    private Answer? WhenNoCandidateHoldsTheAnswer(
        string text,
        string rewritten,
        (PhraseCorrection Correction, string Text)? trusted,
        IReadOnlyList<string> remaps)
    {
        if (trusted is { } advice)
            return Answer.FromDictionary(advice.Correction);

        if (DictionaryDeclined(rewritten))
            return null;

        var inScript = Scripts.Dominant(text);
        var nearEcho = inScript is not null &&
                       inScript == Scripts.Dominant(rewritten) &&
                       LooksLikeWrongLayoutRun(text);

        return Answer.FromModelOrRemap(nearEcho && remaps.Count > 0 ? remaps[0] : rewritten);
    }

    private readonly record struct Answer(string Text, PhraseCorrection? Correction)
    {
        public static Answer FromDictionary(PhraseCorrection correction) => new(correction.Corrected, correction);

        public static Answer FromModelOrRemap(string text) => new(text, null);
    }

    private (PhraseCorrection Correction, string Text)? Advice(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred)
    {
        if (_adviser is null || !_adviser.IsReady)
            return null;

        var correction = _adviser.CorrectPhrase(text, active, installed, candidates, preferred);
        if (!correction.Changed)
            return null;

        var trimmed = correction.Corrected.Trim();
        return trimmed.Length == 0 || string.Equals(trimmed, text.Trim(), StringComparison.Ordinal)
            ? null
            : (correction, trimmed);
    }

    private bool JudgesEveryReading(LayoutSource active, IReadOnlyList<LayoutCandidate> candidates)
    {
        if (_adviser is not { IsReady: true } adviser)
            return false;

        if (active.Script is { } typed && !adviser.Knows(typed))
            return false;

        foreach (var candidate in candidates)
            if (!adviser.Knows(candidate.ScoringScript))
                return false;

        return true;
    }

    private static bool IsOneWord(string text)
    {
        var trimmed = text.AsSpan().Trim();
        foreach (var ch in trimmed)
            if (char.IsWhiteSpace(ch))
                return false;

        return trimmed.Length > 0;
    }

    private bool DictionaryDeclined(string rewritten)
    {
        return _adviser is { IsReady: true } adviser &&
               Scripts.Dominant(rewritten) is { } script &&
               adviser.Knows(script);
    }

    private static IReadOnlyList<string> Offer(
        (PhraseCorrection Correction, string Text)? advice, IReadOnlyList<string> remaps)
    {
        if (advice is not { } hint)
            return remaps;

        if (remaps.Count > 0 && string.Equals(remaps[0], hint.Text, StringComparison.Ordinal))
            return remaps;

        var offered = new List<string>(remaps.Count + 1) { hint.Text };
        foreach (var remap in remaps)
            if (!string.Equals(remap, hint.Text, StringComparison.Ordinal))
                offered.Add(remap);

        return offered;
    }

    private static bool IsEcho(string rewritten, string text)
    {
        return string.Equals(rewritten, text, StringComparison.Ordinal) ||
               string.Equals(rewritten, text.Trim(), StringComparison.Ordinal);
    }

    private static string RestoreOuterWhitespace(string original, string rewritten)
    {
        var trimmed = original.AsSpan().Trim();
        if (trimmed.Length == original.Length || trimmed.Length == 0)
            return rewritten;

        var leading = original.Length - original.AsSpan().TrimStart().Length;
        var trailing = original.Length - original.AsSpan().TrimEnd().Length;
        return string.Concat(original.AsSpan(0, leading), rewritten, original.AsSpan(original.Length - trailing));
    }

    private static bool LooksLikeWrongLayoutRun(string text)
    {
        if (Scripts.Dominant(text) is not { } script)
            return false;

        if (!Orthography.IsPossibleWord(text, script))
            return true;

        if (Scripts.IsScoringBlind(script))
            return false;

        var letters = 0;
        var vowels = 0;
        foreach (var ch in text)
        {
            if (!Alphabets.IsLetterOf(script, ch))
                continue;

            letters++;
            if (Alphabets.IsVowelOf(script, ch))
                vowels++;
        }

        return letters >= 3 && vowels == 0;
    }

    public static string SanitizeModelOutput(string raw, string original)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var s = raw.Trim();

        // Refuse multi-paragraph chatter.
        var nl = s.IndexOfAny(['\r', '\n']);
        if (nl >= 0)
            s = s[..nl].Trim();

        if (s.Length >= 2 &&
            ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();

        if (s.StartsWith("=>", StringComparison.Ordinal))
            s = s[2..].Trim();

        foreach (var prefix in new[]
                 {
                     "Corrected:", "Correction:", "Output:", "Answer:", "Result:",
                     "Исправленный текст:", "Исправлено:", "Ответ:"
                 })
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[prefix.Length..].Trim();
                break;
            }

        var arrowIdx = s.LastIndexOf("=>", StringComparison.Ordinal);
        var arrowLen = 2;
        if (arrowIdx < 0)
        {
            arrowIdx = s.LastIndexOf("→", StringComparison.Ordinal);
            arrowLen = 1;
        }

        if (arrowIdx >= 0 && arrowIdx + arrowLen < s.Length)
        {
            var right = s[(arrowIdx + arrowLen)..].Trim();
            if (right.Length > 0)
                s = right;
        }

        if (s.Length > Math.Max(original.Length * 3, original.Length + 40))
            return string.Empty;

        return s;
    }

    public static (KeyboardLayout? Target, string? LayoutId) ResolveTarget(
        string original,
        string corrected,
        LayoutSource active,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred)
    {
        var originalScript = Scripts.Dominant(original) ?? active.Script;
        if (MovedInto(original, corrected, originalScript) is not { } correctedScript)
            return (null, null);

        LayoutCandidate? match = null;
        foreach (var c in candidates)
        {
            if (c.ScoringScript != correctedScript)
                continue;

            if (preferred is not null && c.Target == preferred)
            {
                match = c;
                break;
            }

            match ??= c;
        }

        return match is null ? (null, null) : (match.Value.Target, match.Value.LayoutId);
    }

    private static Script? MovedInto(string original, string corrected, Script? originalScript)
    {
        var before = original.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var after = corrected.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (before.Length == 0 || before.Length != after.Length)
        {
            var whole = Scripts.Dominant(corrected);
            return whole is null || whole == originalScript ? null : whole;
        }

        Script? into = null;
        for (var i = 0; i < before.Length; i++)
        {
            var to = Scripts.Dominant(after[i]);
            if (to is null || to == Scripts.Dominant(before[i]))
                continue;

            if (into is not null && into != to)
                return null;

            into = to;
        }

        return into;
    }
}
