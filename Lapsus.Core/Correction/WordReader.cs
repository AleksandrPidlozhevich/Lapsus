using Lapsus.Core.Layout;
using System.Text;
using static Lapsus.Core.Correction.WordSources;

namespace Lapsus.Core.Correction;

internal sealed class WordReader
{
    private const double EditPenalty = 0.12;

    // Share of the commonest end of the list a one-edit fix must reach to replace keys no dictionary accepts.
    private const double MinFrequencyToFixPlainKeys = 0.4;

    // A word typed in its own script is respelled through one slip (a neighbour, a swap, a letter dropped or
    // extra) into any listed word; through a dearer one (a key from across the board, two slips) only into a
    // word this common. Measured on the Russian lists: such real fixes ("кароче") land on 0.55 and up, unknown
    // words wrongly respelled ("ревью" → речью) on 0.43 and down.
    private const double MaxCheapSlipCost = TypoCost.Extra;

    private const double MaxSlipCostToRespell = 1.6;

    private const double MinFrequencyToRespell = 0.5;

    // The most a word no list knows can score (naturalness × WordScorer's unknown-word penalty).
    private const double UnknownWordCeiling = 0.6;

    private const int MinWordToKeepPunctuation = 3;

    private const int MinWordToKeepSentencePunctuation = 2;

    // Below 5 letters a clitic is most of the word.
    private const int MinLettersToCrossOnClitic = 5;

    // Trigram alone may follow a settled line; never with no direction.
    private const int MinLettersToFollowTheLine = 4;

    private const double MinNaturalnessToFollowTheLine = 0.7;

    private readonly double _switchThreshold;
    private readonly double _punctuationHeadStart;
    private readonly WordScorer _scorer;
    private readonly WordExceptions _exceptions;

    public WordReader(WordScorer scorer, WordExceptions exceptions, double switchThreshold, double punctuationHeadStart)
    {
        _scorer = scorer;
        _exceptions = exceptions;
        _switchThreshold = switchThreshold;
        _punctuationHeadStart = punctuationHeadStart;
    }

    // "don't" typed on another layout: the apostrophe key ends the word there ("δον'τ", "גםמ,א"), and each
    // half would be read alone. The halves are one word only when together they spell a listed contraction.
    public int EndOfContraction(string text, int wordStart, int wordEnd, WordSource source)
    {
        if (source.Script == Script.Latin || wordEnd + 1 >= text.Length || !char.IsLetter(text[wordEnd + 1]) ||
            !source.Map.TryGetKey(text[wordEnd], out var slot, out var shift))
            return wordEnd;

        var end = wordEnd + 1;
        while (end < text.Length && char.IsLetter(text[end]))
            end++;

        foreach (var candidate in source.Candidates)
        {
            if (candidate.ScoringScript != Script.Latin || candidate.Map.CharAtSlot(slot, shift) != '\'')
                continue;

            var reading = LayoutTranscoder.Transcode(text[wordStart..end], source.Map, candidate.Map);
            if (WordScorer.IsDictionaryHit(_scorer.Score(reading, Script.Latin, candidate.LanguageCode)))
                return end;
        }

        return wordEnd;
    }
    public (ScoredWord Best, int End) ExtendOverTrailingRun(
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

        // A listed word left as typed keeps its mark unless the longer reading is far commoner: the lists
        // hold foreign scraps ("seu"), and "seu," is דקות to anyone typing Hebrew.
        var plainAsTyped = plainIsWord && !best.Changed;

        // As BestWord: the keys alone may cross into an abjad on a clitic's stem (ההגדרות off הגדרות).
        var word = text[wordStart..wordEnd];
        var cliticAlone = !(Scripts.IsScoringBlind(source.Script) && Orthography.IsPossibleWord(word, source.Script));

        // Two-letter hit gives up "."/"," only to a commoner word; never "]" / ";" (often letters).
        var weighsSentencePunctuation = letters >= MinWordToKeepSentencePunctuation;

        for (var end = runEnd; end > wordEnd; end--)
        {
            var endsSentence = EndsSentence(text, wordEnd, end);
            var token = text[wordStart..end];
            if (_exceptions.Contains(token))
                continue;

            if ((ReadAsOneWord(token, source, preferred, cliticAlone: cliticAlone) ??
                 LetterKeysAfterACrossing(best, text[wordEnd..end], source)) is not { } longer)
                continue;

            if ((endsSentence || plainAsTyped) &&
                !KeyIsALetter(best, longer, source, plainIsWord, weighsSentencePunctuation, plainAsTyped))
                continue;

            return (longer, end);
        }

        return (best, wordEnd);
    }

    // A word that crossed takes the letter keys after it whatever the list says: to someone typing Russian
    // ";" "'" "[" "]" are ж э х ъ, never punctuation ("vbyf;" is минаж, not мина;). A full stop or comma is
    // still the list's call.
    private static ScoredWord? LetterKeysAfterACrossing(ScoredWord best, string tail, WordSource source)
    {
        if (!best.Changed || CandidateOf(best, source) is not { } candidate || candidate.ScoringScript == source.Script ||
            tail.AsSpan().IndexOfAny(".,!?:") >= 0)
            return null;

        var text = best.Text + LayoutTranscoder.Transcode(tail, source.Map, candidate.Map);
        return WordScanner.IsAllLetters(text, candidate.ScoringScript) ? best with { Text = text } : null;
    }

    private bool KeyIsALetter(
        ScoredWord best, ScoredWord longer, WordSource source, bool plainIsWord, bool weighsSentencePunctuation,
        bool plainAsTyped = false)
    {
        var script = ScriptOf(longer, source);
        if (plainIsWord)
        {
            var longerWord = _scorer.LogCountOf(longer.Text, script, LanguageOf(longer, source));
            var shorterWord = _scorer.LogCountOf(best.Text, ScriptOf(best, source), LanguageOf(best, source) ?? source.LanguageCode);

            // A switched word gives the mark up to a reading up to the head start rarer. A word standing as
            // typed gives it up only to one about thirty times commoner: "muse," and "dvd]" are English.
            return longerWord - shorterWord > (plainAsTyped ? 2 * _punctuationHeadStart : -_punctuationHeadStart);
        }

        return !weighsSentencePunctuation ||
               _scorer.FrequencyOf(longer.Text, script) >= _scorer.FrequencyOf(best.Text, ScriptOf(best, source));
    }
    public ScoredWord? FixInOwnScript(in TokenReading token)
    {
        if (!_scorer.CanSpellFix || !WordScanner.IsAllLetters(token.Typed, token.Source.Script))
            return null;

        var (fixedText, edits) = _scorer.SpellFix(
            token.Typed, token.Source.Script, token.Source.LanguageCode, typedInScript: true, token.Source.Map);
        if (edits != 1)
            return null;

        var score = _scorer.Score(fixedText, token.Source.Script, token.Source.LanguageCode) - EditPenalty * edits;
        return new ScoredWord(fixedText, null, null, score, true, edits);
    }

    public ScoredWord? ReadAsOneWord(
        string token, WordSource source, KeyboardLayout? preferred, Script? only = null, bool cliticAlone = false)
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

            // Clitic evidence only with a settled line direction, or where the caller crosses on the keys
            // alone as BestWord does (cliticAlone); otherwise it must not pick a script.
            var clitic = (only is not null || cliticAlone) && Scripts.IsAbjad(candidate.ScoringScript) &&
                         WordScorer.IsWeakDictionaryHit(score) &&
                         WordScanner.TrimToLetters(switched, candidate.ScoringScript).Length >= MinLettersToCrossOnClitic;

            var followsTheLine = only is not null && !clitic && !WordScorer.IsDictionaryHit(score) &&
                                 WordScanner.TrimToLetters(switched, candidate.ScoringScript).Length >= MinLettersToFollowTheLine &&
                                 _scorer.NaturalnessOf(switched, candidate.ScoringScript, candidate.LanguageCode)
                                     >= MinNaturalnessToFollowTheLine;

            if (!WordScorer.IsDictionaryHit(score) && !clitic && !followsTheLine)
                continue;

            // A word only the line carries across, one edit from a common listed word, is a slip.
            var edits = 0;
            if (followsTheLine && _scorer.CanSpellFix)
            {
                var (fixedWord, fixEdits) = _scorer.SpellFix(
                    switched, candidate.ScoringScript, candidate.LanguageCode, map: candidate.Map);
                var fixedScore = fixEdits == 1
                    ? _scorer.Score(fixedWord, candidate.ScoringScript, candidate.LanguageCode)
                    : 0.0;
                if (WordScorer.IsDictionaryHit(fixedScore) && IsASlipNotARareWord(fixedWord, switched, candidate))
                {
                    switched = fixedWord;
                    score = fixedScore - EditPenalty;
                    edits = fixEdits;
                }
            }

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
                best = new ScoredWord(switched, candidate.Target, candidate.LayoutId, score, true, edits);
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
    private static bool EndsSentence(string text, int from, int to)
    {
        for (var i = from; i < to; i++)
            if (text[i] is '.' or ',' or '!' or '?')
                return true;

        return false;
    }

    public ScoredWord CorrectWord(string word, WordSource source, KeyboardLayout? preferred, bool keysOnly)
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

        // "];f" spells "f" once its leading keys are trimmed, but as typed it is no word: those keys are letters
        // elsewhere (їжа). It scores as an unknown word, so a listed reading can cross it. A leading apostrophe
        // is English's own ('s, 'll), not such a key.
        if (word.Length > 0 && !char.IsLetter(word[0]) && word[0] is not ('\'' or '’'))
            baseline = Math.Min(baseline, UnknownWordCeiling);
        var best = ScoredWord.AsTyped(word, baseline);

        var writableAsTyped = Orthography.IsPossibleWord(word, source.Script);

        var blindSource = Scripts.IsScoringBlind(source.Script) && writableAsTyped;

        void Consider(string text, Script script, KeyboardLayout? target, string? languageCode,
            string? layoutId, int edits, bool requireDictionaryHit = false)
        {
            if (text == word)
                return;

            // An edit-requiring candidate must never outscore and overwrite an earlier edit-free one:
            // that would veto the whole word down to the untouched original below, instead of the
            // plain keys-only reading still sitting in `best`.
            if (keysOnly && edits > 0)
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
            var (fixedSame, edits) = _scorer.SpellFix(
                word, source.Script, source.LanguageCode, typedInScript: true, source.Map);
            if (edits == 0 || IsASlipOfACommonWord(word, fixedSame, source))
                Consider(fixedSame, source.Script, null, source.LanguageCode, null, edits);
            // A respell declined still says the word is of its own script: another layout must beat the
            // respell it would have been, or the word stays as typed.
            else if (!keysOnly && best.Changed &&
                     best.Score <= _scorer.Score(fixedSame, source.Script, source.LanguageCode) - EditPenalty * edits)
                return ScoredWord.AsTyped(word, baseline);
        }

        if (!best.Changed || best.Score <= baseline + _switchThreshold)
            return ScoredWord.AsTyped(word, baseline);

        return KeysAsTheyStand(word, source, best);
    }

    // A word the lists lack, typed in its own script, is respelled only as far as a finger slip could have made
    // it: "сегодя" is сегодня, but "вкусняшки" (a key across the board) is no slip for the rare "вкусняшка",
    // nor "созвон" for "позвоню".
    private bool IsASlipOfACommonWord(string typed, string fixedWord, WordSource source)
    {
        var cost = TypoCost.Between(Bare(typed), Bare(fixedWord), source.Map);
        return cost <= MaxCheapSlipCost + 1e-9 ||
               cost <= MaxSlipCostToRespell &&
               _scorer.FrequencyOf(fixedWord, source.Script, source.LanguageCode) >= MinFrequencyToRespell;

        // An accent left off ("νεο" for νερό) is how people type, not a key missed. Not in Cyrillic, where й, ї
        // and ў have keys of their own.
        string Bare(string word)
        {
            if (source.Script == Script.Cyrillic)
                return word.ToLowerInvariant();

            var sb = new StringBuilder(word.Length);
            foreach (var ch in word.ToLowerInvariant().Normalize(NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) !=
                    System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }

    // Keys no list, stem or Hunspell form accepts, one edit from a common listed word: a slip, not a rare word.
    // shortcut: measured on Ukrainian only; the same rule costs 3-40 pp of rare-word recovery in the other languages.
    private bool IsASlipNotARareWord(string fixedWord, string plain, LayoutCandidate candidate)
    {
        return candidate.LanguageCode == "uk" && _scorer.HasLexiconFor("uk") &&
               _scorer.FrequencyOf(fixedWord, candidate.ScoringScript, candidate.LanguageCode) >=
               MinFrequencyToFixPlainKeys &&
               _scorer.Score(plain, candidate.ScoringScript, candidate.LanguageCode) < WordScorer.StemHitFloor;
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

        if (best.Edits == 1 && WordScorer.IsDictionaryHit(best.Score + EditPenalty) &&
            IsASlipNotARareWord(best.Text, plain, candidate))
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
    private ScoredWord? TryBestSplit(string word, WordSource source, KeyboardLayout? preferred, bool keysOnly)
    {
        var best = default(ScoredWord);
        var found = false;

        for (var i = 3; i <= word.Length - 3; i++)
        {
            var left = BestWord(word[..i], source, preferred, keysOnly);
            var right = BestWord(word[i..], source, preferred, keysOnly);

            // A split is for two exact words typed on two layouts ("audiorfdf" is audioкава). Typo fixes glued back
            // together only mangle a word the list lacks: "something" would come out "sothing" or "съмthing".
            if ((!SwitchesLayout(left) && !SwitchesLayout(right)) || !IsExactWord(left) || !IsExactWord(right))
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

    private static bool SwitchesLayout(in ScoredWord half)
    {
        return half.Changed && (half.Target is not null || !string.IsNullOrEmpty(half.TargetLayoutId));
    }

    private static bool IsExactWord(in ScoredWord half)
    {
        return half.Edits == 0 && WordScorer.IsDictionaryHit(half.Score);
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
}
