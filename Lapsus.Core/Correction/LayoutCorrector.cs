using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using static Lapsus.Core.Correction.WordSources;

namespace Lapsus.Core.Correction;

public sealed class LayoutCorrector : IPhraseCorrector
{
    // ln of list count; ~5.5x rarer keeps the mark.
    public const double DefaultPunctuationHeadStart = 1.7;

    // A pool for the neural brain to price by keyboard; frequency alone ranks "утрам" fourth for "утрм".
    private const int MaxSpellSuggestions = 12;

    private readonly WordScorer _scorer;
    private readonly WordExceptions _exceptions;
    private readonly WordReader _reader;
    private readonly bool _phraseContext;

    public LayoutCorrector(
        SpellChecker? spellChecker, double switchThreshold = 0.15, WordExceptions? exceptions = null,
        bool phraseContext = true, double punctuationHeadStart = DefaultPunctuationHeadStart)
    {
        _scorer = new WordScorer(spellChecker);
        _exceptions = exceptions ?? new WordExceptions();
        _reader = new WordReader(_scorer, _exceptions, switchThreshold, punctuationHeadStart);
        _phraseContext = phraseContext;
    }

    public bool IsReady => _scorer.HasDictionaries;

    public bool SupportsLayoutCycle => true;

    public bool PreferAsync => false;

    public bool SupportsAutoMode => true;

    public bool Knows(Script script)
    {
        return _scorer.HasDictionaryFor(script);
    }

    public bool KnowsWord(string word, Script script)
    {
        return _scorer.IsListedWord(word, script);
    }

    public bool AcceptsSpelling(string word, Script script)
    {
        return _scorer.IsListedWord(word, script) || _scorer.IsLexiconForm(word, script);
    }

    public IReadOnlyList<(string Word, int Edits)> SpellSuggestions(string word, Script script)
    {
        return _scorer.SpellSuggestions(word, script, MaxSpellSuggestions);
    }

    public PhraseCorrection CorrectPhrase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new PhraseCorrection(text, text, false, null);

        return CorrectTokens(text, null, null, ch =>
        {
            var script = Scripts.Of(ch) ?? Script.Latin;
            return new WordSource(script, BundledMapFor(script), ScriptLayouts.CandidatesFor(script), null);
        });
    }

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
        if (string.IsNullOrEmpty(text) || candidates.Count == 0)
            return new PhraseCorrection(text, text, false, null);

        return CorrectTokens(text, preferred, null, ch =>
        {
            var script = Scripts.Of(ch) ?? active.Script ?? Script.Latin;
            var (map, languageCode) = ResolveSource(script, active, installed, preferred);
            return new WordSource(script, map, candidates, languageCode);
        }, installed, hints);
    }

    public PhraseCorrection CorrectPhrase(
        string text, Script sourceScript, KeyboardMap sourceMap,
        IReadOnlyList<LayoutCandidate> candidates, KeyboardLayout? preferred = null,
        string? sourceLanguageCode = null)
    {
        if (string.IsNullOrEmpty(text) || candidates.Count == 0)
            return new PhraseCorrection(text, text, false, null);

        var source = new WordSource(sourceScript, sourceMap, candidates, sourceLanguageCode);
        return CorrectTokens(text, preferred, sourceLanguageCode, _ => source);
    }

    private PhraseCorrection CorrectTokens(
        string text, KeyboardLayout? preferred, string? phraseSourceLanguageCode,
        Func<char, WordSource> sourceFor,
        IReadOnlyList<LayoutSource>? installed = null,
        CorrectionHints hints = default)
    {
        var context = new PhraseContext(
            _scorer,
            (token, source, script) => _reader.ReadAsOneWord(token, source, preferred, script),
            _reader.FixInOwnScript,
            hints.LineDirection,
            hints.KeysOnly);

        // Match listed chunks before WordScanner; it would split "we-on" at a non-letter key.
        var excluded = _exceptions.IsEmpty ? null : _exceptions.ChunkSpans(text);

        IReadOnlyList<TokenReading> readings = ReadTokens(
            text, preferred, phraseSourceLanguageCode, sourceFor, installed, excluded, hints.KeysOnly, hints.TypoOnly);
        if (_phraseContext)
        {
            foreach (var reading in readings)
                context.Add(reading);

            readings = context.Revise();
        }

        return PhraseEmitter.Emit(_scorer, text, readings, preferred, excluded);
    }

    // typoOnly leaves every word without other layouts to read it as, so no switch can be chosen at all.
    private List<TokenReading> ReadTokens(
        string text, KeyboardLayout? preferred, string? phraseSourceLanguageCode,
        Func<char, WordSource> sourceFor,
        IReadOnlyList<LayoutSource>? installed,
        IReadOnlyList<TextSpan>? excluded,
        bool keysOnly,
        bool typoOnly)
    {
        var readings = new List<TokenReading>();
        var excludedMask = excluded is null ? null : TextSpans.BuildMask(text.Length, excluded);
        var verbatimStart = -1;
        var i = 0;

        void OpenVerbatim(int at)
        {
            if (verbatimStart < 0)
                verbatimStart = at;
        }

        void CloseVerbatim(int end)
        {
            if (verbatimStart < 0)
                return;

            readings.Add(TokenReading.Verbatim(text[verbatimStart..end]));
            verbatimStart = -1;
        }

        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                OpenVerbatim(i++);
                continue;
            }

            // Whole letterless chunk whose keys are letters elsewhere; only the line may pull it in.
            var atChunkStart = i == 0 || char.IsWhiteSpace(text[i - 1]);
            if (!typoOnly && atChunkStart &&
                LetterlessChunkEnd(text, i, sourceFor, installed, excludedMask) is { } chunkEnd)
            {
                CloseVerbatim(i);
                var chunk = text[i..chunkEnd];
                var chunkSource = KeysSource(chunk, sourceFor, installed) with { LanguageCode = phraseSourceLanguageCode };
                chunkSource = chunkSource with
                {
                    Candidates = EnsureOppositeScriptCandidates(chunkSource.Script, chunkSource.Candidates, installed)
                };
                readings.Add(new TokenReading(
                    chunk, chunkSource, 0.0, 0.0, ScoredWord.AsTyped(chunk, 0.0), chunkSource.Script, true));
                i = chunkEnd;
                continue;
            }

            var firstLetter = WordScanner.IndexOfFirstLetter(text, i);
            if (firstLetter < 0)
            {
                OpenVerbatim(i++);
                continue;
            }

            var raw = sourceFor(text[firstLetter]);
            var source = raw with
            {
                Candidates = typoOnly
                    ? Array.Empty<LayoutCandidate>()
                    : EnsureOppositeScriptCandidates(raw.Script, raw.Candidates, installed),
                LanguageCode = raw.LanguageCode ?? phraseSourceLanguageCode
            };

            var wordStart = i;
            var scan = i;
            // Dead key before the first letter opens the word: ";exv" is έχω.
            while (scan < firstLetter &&
                   (WordScanner.CarriesLetterElsewhere(text[scan], source.Map, source.Candidates) ||
                    WordScanner.AccentsTheNextLetter(text, scan, source.Map, source.Candidates)))
                scan++;

            if (scan < firstLetter)
            {
                OpenVerbatim(wordStart);
                i = scan + 1;
                continue;
            }

            i = WordScanner.EndOfLayoutWord(text, firstLetter, source.Script, source.Map, source.Candidates);
            i = _reader.EndOfContraction(text, wordStart, i, source);

            var inExcludedChunk = excludedMask is not null && excludedMask[firstLetter];
            ScoredWord best;
            if (inExcludedChunk)
            {
                var asTyped = text[wordStart..i];
                best = ScoredWord.AsTyped(asTyped, _scorer.Score(asTyped, source.Script, source.LanguageCode));
            }
            else
            {
                best = _reader.CorrectWord(text[wordStart..i], source, preferred, keysOnly);

                // Typos only: a fix must land on a word the dictionaries know. Without other layouts to read,
                // a guessed or split spelling would otherwise turn a Ukrainian word into Latin noise.
                if (typoOnly && best.Changed &&
                    !WordScorer.IsDictionaryHit(_scorer.Score(best.Text, source.Script, source.LanguageCode)))
                {
                    var typedAsIs = text[wordStart..i];
                    best = ScoredWord.AsTyped(typedAsIs, _scorer.Score(typedAsIs, source.Script, source.LanguageCode));
                }

                (best, i) = _reader.ExtendOverTrailingRun(text, wordStart, i, best, source, preferred);
            }

            CloseVerbatim(wordStart);

            var typed = text[wordStart..i];
            var baseline = best.Changed
                ? _scorer.Score(typed, source.Script, source.LanguageCode)
                : best.Score;

            readings.Add(new TokenReading(
                typed, source, baseline, _scorer.FrequencyOf(typed, source.Script), best,
                Scripts.Dominant(best.Text) ?? source.Script, true,
                inExcludedChunk || _exceptions.Contains(typed)));
        }

        CloseVerbatim(text.Length);
        return readings;
    }

    private (KeyboardMap Map, string? LanguageCode) ResolveSource(
        Script wordScript, LayoutSource active, IReadOnlyList<LayoutSource> installed, KeyboardLayout? preferred)
    {
        if (active.Script == wordScript)
            return (active.Map, active.LanguageCode);

        foreach (var layout in installed)
            if (layout.Script == wordScript)
                return (layout.Map, layout.LanguageCode);

        return (BundledMapFor(wordScript, preferred), null);
    }

    private KeyboardMap BundledMapFor(Script script, KeyboardLayout? preferred = null)
    {
        if (preferred is { } p && ScriptLayouts.ScriptOf(p) == script)
            return ScriptLayouts.MapFor(p);

        foreach (var code in _scorer.LanguagesFor(script))
            if (LayoutLanguage.ToKeyboardLayout(code) is { } fromDict)
                return ScriptLayouts.MapFor(fromDict);

        return ScriptLayouts.FallbackMapFor(script);
    }
}
