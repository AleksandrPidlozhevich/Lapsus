using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using System.Text;

namespace Lapsus.Core.Correction;

public sealed class LayoutCorrector : IPhraseCorrector
{
    private const double EditPenalty = 0.12;

    private const int MinWordToKeepPunctuation = 3;

    private const int MinWordToKeepSentencePunctuation = 2;

    // ln of list count; ~5.5x rarer keeps the mark.
    public const double DefaultPunctuationHeadStart = 1.7;

    // Short words must not settle a line: "d"/"vs" are also how "в"/"мы" get typed.
    private const int MinWordToSettle = 3;

    // Below 5 letters a clitic is most of the word.
    private const int MinLettersToCrossOnClitic = 5;

    // Trigram alone may follow a settled line; never with no direction.
    private const int MinLettersToFollowTheLine = 4;

    private const double MinNaturalnessToFollowTheLine = 0.7;

    // A pool for the neural brain to price by keyboard; frequency alone ranks "утрам" fourth for "утрм".
    private const int MaxSpellSuggestions = 12;

    private readonly double _switchThreshold;
    private readonly double _punctuationHeadStart;
    private readonly WordScorer _scorer;
    private readonly WordExceptions _exceptions;
    private readonly bool _phraseContext;

    public LayoutCorrector(
        SpellChecker? spellChecker, double switchThreshold = 0.15, WordExceptions? exceptions = null,
        bool phraseContext = true, double punctuationHeadStart = DefaultPunctuationHeadStart)
    {
        _scorer = new WordScorer(spellChecker);
        _switchThreshold = switchThreshold;
        _punctuationHeadStart = punctuationHeadStart;
        _exceptions = exceptions ?? new WordExceptions();
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
            (token, source, script) => ReadAsOneWord(token, source, preferred, script),
            FixInOwnScript,
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

        return Emit(text, RetypeGluedPunctuation(text, readings, excluded), preferred, excluded);
    }

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
                var chunkSource = sourceFor(chunk[0]) with { LanguageCode = phraseSourceLanguageCode };
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

            var inExcludedChunk = excludedMask is not null && excludedMask[firstLetter];
            ScoredWord best;
            if (inExcludedChunk)
            {
                var asTyped = text[wordStart..i];
                best = ScoredWord.AsTyped(asTyped, _scorer.Score(asTyped, source.Script, source.LanguageCode));
            }
            else
            {
                best = CorrectWord(text[wordStart..i], source, preferred, keysOnly);

                // Typos only: a fix must land on a word the dictionaries know. Without other layouts to read,
                // a guessed or split spelling would otherwise turn a Ukrainian word into Latin noise.
                if (typoOnly && best.Changed &&
                    !WordScorer.IsDictionaryHit(_scorer.Score(best.Text, source.Script, source.LanguageCode)))
                {
                    var typedAsIs = text[wordStart..i];
                    best = ScoredWord.AsTyped(typedAsIs, _scorer.Score(typedAsIs, source.Script, source.LanguageCode));
                }

                (best, i) = ExtendOverTrailingRun(text, wordStart, i, best, source, preferred);
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

    private static int? LetterlessChunkEnd(
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

        var raw = sourceFor(text[start]);
        var candidates = EnsureOppositeScriptCandidates(raw.Script, raw.Candidates, installed);
        for (var k = start; k < end; k++)
            if (!WordScanner.CarriesLetterElsewhere(text[k], raw.Map, candidates))
                return null;

        return end;
    }

    private static PhraseCorrection Emit(
        string text, IReadOnlyList<TokenReading> readings, KeyboardLayout? preferred,
        IReadOnlyList<TextSpan>? excluded)
    {
        var sb = new StringBuilder(text.Length);
        var votes = new TargetVotes();

        List<TextSpan>? settled = excluded is null ? null : [.. excluded];
        var at = 0;

        foreach (var token in readings)
        {
            sb.Append(token.Chosen.Text);

            var start = at;
            at += token.Typed.Length;

            if (!token.IsWord)
                continue;

            if (token.IsException)
                (settled ??= []).Add(new TextSpan(start, token.Typed.Length, true));
            else if (!token.Chosen.Changed && WordScorer.IsDictionaryHit(token.Baseline) &&
                     WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length >= MinWordToSettle)
                // Advisory only while part of the line still needs work; drop once every letter is settled.
                (settled ??= []).Add(new TextSpan(start, token.Typed.Length));

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

        var corrected = sb.ToString();
        var (targetLayout, targetLayoutId) = votes.Pick(preferred);

        // If contiguity breaks, report no spans rather than wrong ones.
        if (at != text.Length)
            settled = null;

        return new PhraseCorrection(
            text, corrected, !string.Equals(corrected, text, StringComparison.Ordinal),
            targetLayout, targetLayoutId, settled);
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

    private (ScoredWord Best, int End) ExtendOverTrailingRun(
        string text, int wordStart, int wordEnd, ScoredWord best, WordSource source, KeyboardLayout? preferred)
    {
        var runEnd = WordScanner.EndOfTrailingLayoutRun(text, wordEnd, source.Map, source.Candidates);
        if (runEnd == wordEnd)
            return (best, wordEnd);

        if (_exceptions.Contains(text[wordStart..wordEnd]))
            return (best, wordEnd);

        var letters = WordScorer.IsDictionaryHit(best.Score)
            ? WordScanner.TrimToLetters(best.Text, ScriptOf(best, source)).Length
            : 0;
        var plainIsWord = letters >= MinWordToKeepPunctuation;

        if (plainIsWord && !best.Changed)
            return (best, wordEnd);

        // Two-letter hit gives up "."/"," only to a commoner word; never "]" / ";" (often letters).
        var weighsSentencePunctuation = letters >= MinWordToKeepSentencePunctuation;

        for (var end = runEnd; end > wordEnd; end--)
        {
            var endsSentence = EndsSentence(text, wordEnd, end);
            var token = text[wordStart..end];
            if (_exceptions.Contains(token))
                continue;

            if (ReadAsOneWord(token, source, preferred) is not { } longer)
                continue;

            if (endsSentence && !KeyIsALetter(best, longer, source, plainIsWord, weighsSentencePunctuation))
                continue;

            return (longer, end);
        }

        return (best, wordEnd);
    }

    private bool KeyIsALetter(
        ScoredWord best, ScoredWord longer, WordSource source, bool plainIsWord, bool weighsSentencePunctuation)
    {
        var script = ScriptOf(longer, source);
        if (plainIsWord)
        {
            var longerWord = _scorer.LogCountOf(longer.Text, script, LanguageOf(longer, source));
            var shorterWord = _scorer.LogCountOf(best.Text, ScriptOf(best, source), LanguageOf(best, source) ?? source.LanguageCode);
            return longerWord - shorterWord > -_punctuationHeadStart;
        }

        return !weighsSentencePunctuation ||
               _scorer.FrequencyOf(longer.Text, script) >= _scorer.FrequencyOf(best.Text, ScriptOf(best, source));
    }

    private static string? LanguageOf(ScoredWord reading, WordSource source)
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

    private ScoredWord? FixInOwnScript(in TokenReading token)
    {
        if (!_scorer.CanSpellFix || !WordScanner.IsAllLetters(token.Typed, token.Source.Script))
            return null;

        var (fixedText, edits) = _scorer.SpellFix(token.Typed, token.Source.Script, token.Source.LanguageCode);
        if (edits != 1)
            return null;

        var score = _scorer.Score(fixedText, token.Source.Script, token.Source.LanguageCode) - EditPenalty * edits;
        return new ScoredWord(fixedText, null, null, score, true, edits);
    }

    private ScoredWord? ReadAsOneWord(
        string token, WordSource source, KeyboardLayout? preferred, Script? only = null)
    {
        var best = default(ScoredWord);
        var found = false;

        foreach (var candidate in source.Candidates)
        {
            if (candidate.ScoringScript == source.Script)
                continue;

            if (only is { } wanted && candidate.ScoringScript != wanted)
                continue;

            var switched = LayoutTranscoder.Transcode(token, source.Map, candidate.Map);
            if (switched == token)
                continue;

            if (!WordScanner.IsAllLetters(switched, candidate.ScoringScript))
                continue;

            var score = _scorer.Score(switched, candidate.ScoringScript, candidate.LanguageCode);

            // Clitic evidence only with a settled line direction; alone it must not pick a script.
            var clitic = only is not null && Scripts.IsAbjad(candidate.ScoringScript) &&
                         WordScorer.IsWeakDictionaryHit(score) &&
                         WordScanner.TrimToLetters(switched, candidate.ScoringScript).Length >= MinLettersToCrossOnClitic;

            var followsTheLine = only is not null && !clitic && !WordScorer.IsDictionaryHit(score) &&
                                 WordScanner.TrimToLetters(switched, candidate.ScoringScript).Length >= MinLettersToFollowTheLine &&
                                 _scorer.NaturalnessOf(switched, candidate.ScoringScript, candidate.LanguageCode)
                                     >= MinNaturalnessToFollowTheLine;

            if (!WordScorer.IsDictionaryHit(score) && !clitic && !followsTheLine)
                continue;

            var incumbentIsPreferred = preferred is not null && found && best.Target == preferred;
            var breaksTie = found && SameEvidence(score, best.Score) &&
                            candidate.Target == preferred && best.Target != preferred;
            var outranked = score > best.Score &&
                            !(incumbentIsPreferred && SameEvidence(score, best.Score));
            var winsUnknownTie = found && WinsUnknownTie(
                switched, candidate.ScoringScript, candidate.Target, candidate.LanguageCode, score, best, source,
                preferred);
            if (!found || outranked || breaksTie || winsUnknownTie)
            {
                best = new ScoredWord(switched, candidate.Target, candidate.LayoutId, score, true);
                found = true;
            }
        }

        return found ? best : null;
    }

    private static bool SameEvidence(double a, double b)
    {
        return WordScorer.IsDictionaryHit(a) && WordScorer.IsDictionaryHit(b) &&
               Math.Abs(a - b) <= WordScorer.FrequencySpan;
    }

    // Same-script unknown-word tie: prefer the preferred layout, else the better language-fit model.
    private bool WinsUnknownTie(
        string text, Script script, KeyboardLayout? target, string? languageCode, double score,
        ScoredWord incumbent, WordSource source, KeyboardLayout? preferred)
    {
        if (!incumbent.Changed || Math.Abs(score - incumbent.Score) > 1e-9 ||
            WordScorer.IsDictionaryHit(score) || WordScorer.IsWeakDictionaryHit(score) ||
            ScriptOf(incumbent, source) != script)
            return false;

        var incumbentLanguage = LanguageOf(incumbent, source);
        if (incumbentLanguage is null || languageCode is null ||
            string.Equals(incumbentLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
            return false;

        if (preferred is not null && (target == preferred || incumbent.Target == preferred))
            return target == preferred;

        return _scorer.LanguageFit(text, script, languageCode) is { } mine &&
               _scorer.LanguageFit(incumbent.Text, script, incumbentLanguage) is { } theirs &&
               mine > theirs;
    }

    private static Script ScriptOf(ScoredWord best, WordSource source)
    {
        return Scripts.Dominant(best.Text) ?? source.Script;
    }

    private static bool EndsSentence(string text, int from, int to)
    {
        for (var i = from; i < to; i++)
            if (text[i] is '.' or ',' or '!' or '?')
                return true;

        return false;
    }

    private ScoredWord CorrectWord(string word, WordSource source, KeyboardLayout? preferred, bool keysOnly)
    {
        if (_exceptions.Contains(word))
            return ScoredWord.AsTyped(word, _scorer.Score(word, source.Script, source.LanguageCode));

        var best = BestWord(word, source, preferred, keysOnly);
        if (best.Changed || source.Script != Script.Latin || word.Length < 6)
            return best;

        if (WordScorer.IsDictionaryHit(best.Score))
            return best;

        return TryBestSplit(word, source, preferred, keysOnly) ?? best;
    }

    private ScoredWord BestWord(string word, WordSource source, KeyboardLayout? preferred, bool keysOnly = false)
    {
        var baseline = _scorer.Score(word, source.Script, source.LanguageCode);
        var best = ScoredWord.AsTyped(word, baseline);

        var writableAsTyped = Orthography.IsPossibleWord(word, source.Script);

        var blindSource = Scripts.IsScoringBlind(source.Script) && writableAsTyped;

        void Consider(string text, Script script, KeyboardLayout? target, string? languageCode,
            string? layoutId, int edits, bool requireDictionaryHit = false)
        {
            if (text == word)
                return;

            var raw = _scorer.Score(text, script, languageCode);

            var needsDictionary = requireDictionaryHit || blindSource || Scripts.IsScoringBlind(script);

            // Abjad clitic stand-in: keys alone, never on the way out.
            var clitic = edits == 0 && !blindSource && !requireDictionaryHit && Scripts.IsAbjad(script) &&
                         WordScorer.IsWeakDictionaryHit(raw) &&
                         WordScanner.TrimToLetters(text, script).Length >= MinLettersToCrossOnClitic;

            if (needsDictionary && !WordScorer.IsDictionaryHit(raw) && !clitic)
                return;

            var score = raw - EditPenalty * edits;
            var incumbentIsPreferred = preferred is not null && best.Changed && best.Target == preferred;
            var breaksTie = best.Changed && SameEvidence(score, best.Score) &&
                            target == preferred && best.Target != preferred;
            var outranked = score > best.Score &&
                            !(incumbentIsPreferred && SameEvidence(score, best.Score));
            if (outranked || breaksTie ||
                WinsUnknownTie(text, script, target, languageCode, score, best, source, preferred))
                best = new ScoredWord(text, target, layoutId, score, true, edits);
        }

        foreach (var candidate in source.Candidates)
        {
            var sameScript = candidate.ScoringScript == source.Script;

            if (writableAsTyped && !_scorer.HasDictionaryFor(source.Script) &&
                Scripts.IsBlindPair(source.Script, candidate.ScoringScript))
                continue;

            if (sameScript &&
                !LayoutTranscoder.HasMistypedLetterKey(word, source.Script, source.Map, candidate.Map))
                continue;

            var switched = LayoutTranscoder.Transcode(word, source.Map, candidate.Map);
            Consider(switched, candidate.ScoringScript, candidate.Target, candidate.LanguageCode,
                candidate.LayoutId, 0, sameScript);

            // No spell-fix on same-script / blind: it would manufacture the required dictionary hit.
            if (sameScript || blindSource || !_scorer.CanSpellFix)
                continue;

            var (fixedSwitched, edits) = _scorer.SpellFix(switched, candidate.ScoringScript, candidate.LanguageCode);
            Consider(fixedSwitched, candidate.ScoringScript, candidate.Target, candidate.LanguageCode,
                candidate.LayoutId, edits);
        }

        // Skip same-script spell-fix once a keys-alone cross-script switch clears the threshold — but only
        // for a dictionary word: an unknown typo scores near zero, so mere naturalness ("apartmnt" as
        // фзфкеьте) would clear it too and lock out the one-edit fix.
        var crossScriptSwitch = best.Changed && best.Edits == 0 &&
                                (best.Target is not null || !string.IsNullOrEmpty(best.TargetLayoutId)) &&
                                best.Score > baseline + _switchThreshold &&
                                WordScorer.IsDictionaryHit(best.Score);

        if (_scorer.CanSpellFix && !crossScriptSwitch)
        {
            var (fixedSame, edits) = _scorer.SpellFix(word, source.Script, source.LanguageCode);
            Consider(fixedSame, source.Script, null, source.LanguageCode, null, edits);
        }

        if (!best.Changed || best.Score <= baseline + _switchThreshold)
            return ScoredWord.AsTyped(word, baseline);

        // Winning edit still vetoes the switch; it is just not written.
        if (keysOnly && best.Edits > 0)
            return ScoredWord.AsTyped(word, baseline);

        return KeysAsTheyStand(word, source, best);
    }

    private ScoredWord KeysAsTheyStand(string word, WordSource source, ScoredWord best)
    {
        if (best.Edits == 0 || CandidateOf(best, source) is not { } candidate ||
            candidate.ScoringScript == source.Script)
            return best;

        var plain = LayoutTranscoder.Transcode(word, source.Map, candidate.Map);
        if (plain == best.Text || plain == word ||
            !WordScanner.IsAllLetters(plain, candidate.ScoringScript) ||
            WordScanner.TrimToLetters(plain, candidate.ScoringScript).Length < MinLettersToFollowTheLine)
            return best;

        // Keep mark-only edits (e.g. Greek tonos); bare keys are the same word.
        if (DifferOnlyByMarks(plain, best.Text, candidate.ScoringScript))
            return best;

        var natural = _scorer.NaturalnessOf(plain, candidate.ScoringScript, candidate.LanguageCode);
        return natural >= MinNaturalnessToFollowTheLine ? best with { Text = plain } : best;
    }

    private static bool DifferOnlyByMarks(string a, string b, Script script)
    {
        return !string.Equals(a, b, StringComparison.Ordinal) &&
               string.Equals(WithoutMarks(a, script), WithoutMarks(b, script), StringComparison.OrdinalIgnoreCase);
    }

    private static string WithoutMarks(string word, Script script)
    {
        var folded = Scripts.FoldMarks(word, script);
        if (script == Script.Arabic)
            folded = folded.Replace('\u0623', '\u0627').Replace('\u0625', '\u0627').Replace('\u0622', '\u0627');

        try
        {
            var decomposed = folded.Normalize(NormalizationForm.FormD);
            var bare = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                    is not System.Globalization.UnicodeCategory.NonSpacingMark)
                    bare.Append(ch);

            return bare.ToString();
        }
        catch (ArgumentException)
        {
            // Unpaired surrogates; nothing to strip.
            return folded;
        }
        catch (PlatformNotSupportedException)
        {
            // Invariant globalization: no Normalize.
            return folded;
        }
    }

    private static LayoutCandidate? CandidateOf(ScoredWord reading, WordSource source)
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

    private ScoredWord? TryBestSplit(string word, WordSource source, KeyboardLayout? preferred, bool keysOnly)
    {
        var best = default(ScoredWord);
        var found = false;

        for (var i = 3; i <= word.Length - 3; i++)
        {
            var left = BestWord(word[..i], source, preferred, keysOnly);
            var right = BestWord(word[i..], source, preferred, keysOnly);
            if (!left.Changed && !right.Changed)
                continue;

            var score = (left.Score + right.Score) / 2.0;
            if (found && score <= best.Score)
                continue;

            var target = PickSplitTarget(left.Target, right.Target, preferred);
            best = new ScoredWord(
                left.Text + right.Text, target, PickSplitLayoutId(left, right, target), score, true,
                left.Edits + right.Edits);
            found = true;
        }

        if (!found)
            return null;

        var baseline = _scorer.Score(word, source.Script, source.LanguageCode);
        if (best.Score <= baseline + _switchThreshold)
            return null;

        return keysOnly && best.Edits > 0 ? null : best;
    }

    private static string? PickSplitLayoutId(ScoredWord left, ScoredWord right, KeyboardLayout? chosen)
    {
        if (chosen is not null && left.Target == chosen && right.Target != chosen)
            return left.TargetLayoutId;
        if (chosen is not null && right.Target == chosen && left.Target != chosen)
            return right.TargetLayoutId;

        return left.TargetLayoutId ?? right.TargetLayoutId;
    }

    private static KeyboardLayout? PickSplitTarget(
        KeyboardLayout? left, KeyboardLayout? right, KeyboardLayout? preferred)
    {
        if (left is not null && right is not null)
            return left == right ? left : preferred ?? left;

        return left ?? right;
    }

    private static IReadOnlyList<LayoutCandidate> EnsureOppositeScriptCandidates(
        Script sourceScript, IReadOnlyList<LayoutCandidate> candidates, IReadOnlyList<LayoutSource>? installed)
    {
        foreach (var candidate in candidates)
            if (candidate.ScoringScript != sourceScript)
                return candidates;

        var withFallback = new List<LayoutCandidate>(candidates);
        withFallback.AddRange(OppositeScriptFallback(sourceScript, installed));
        return withFallback;
    }

    private static IReadOnlyList<LayoutCandidate> OppositeScriptFallback(
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
