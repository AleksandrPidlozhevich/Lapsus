using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public sealed class NeuralPhraseRewriter : IPhraseCorrector
{
    private readonly ILocalLlm _llm;
    private readonly IPhraseCorrector? _adviser;
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

        var advice = Advice(text, active, installed, candidates, preferred);
        var judgesEverything = JudgesEveryReading(active, candidates);

        var trusted = advice is { } candidate &&
                      (judgesEverything || candidate.Correction.TargetLayout is not null)
            ? advice
            : null;

        if (trusted is { } certain && IsOneWord(text) && judgesEverything)
            return certain.Correction;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(hints.Cancellation);
        budget.CancelAfter(_baseBudget + BudgetPerWord * PhraseChunks.Of(text).Count);
        try
        {
            return AskTheModel(text, active, installed, candidates, preferred, advice, trusted, budget.Token);
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
        CancellationToken cancellationToken)
    {
        var ranked = _ranker.Rank(text, active, installed, candidates, advice?.Correction.Corrected, cancellationToken);
        if (ranked is { } answered)
            return FromRanking(text, answered.Text, active, candidates, preferred, advice, trusted);

        // The model cannot score: the dictionary's answer, as when it runs past its time.
        return advice?.Correction ?? new PhraseCorrection(text, text, false, null);
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

        if (!ModelAnswers.WritesOnlyTypableScripts(text, ranked, active, candidates) || !MovesOnlyIntoKnownWords(text, ranked))
            return trusted?.Correction ?? new PhraseCorrection(text, text, false, null);

        var (target, targetId) = ModelAnswers.ResolveTarget(text, ranked, active, candidates, preferred);
        return new PhraseCorrection(text, ranked, true, target, targetId);
    }


    // A word moved into a script the dictionary reads must be a word it knows; on a phonetic layout the
    // remap of correct English is a transliteration, and the model is happy to pick it.
    private bool MovesOnlyIntoKnownWords(string text, string rewritten)
    {
        if (_adviser is not { IsReady: true } adviser)
            return true;

        var typed = PhraseChunks.Of(text);
        var answered = PhraseChunks.Of(rewritten);
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

    public static (KeyboardLayout? Target, string? LayoutId) ResolveTarget(
        string original, string corrected, LayoutSource active, IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred) =>
        ModelAnswers.ResolveTarget(original, corrected, active, candidates, preferred);
}
