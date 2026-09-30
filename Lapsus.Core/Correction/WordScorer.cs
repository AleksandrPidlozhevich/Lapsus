using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Correction;

internal sealed class WordScorer(SpellChecker? spell)
{
    private const double UnknownWordPenalty = 0.6;

    // Widest frequency can move a score; must stay below the switch threshold.
    public const double FrequencySpan = 0.05;

    private const double OwnLanguageFloor = 0.95;

    private const double ScriptPoolHitScore = 0.92;

    // Keeps commonest pool hit below rarest own-language hit (0.95).
    private const double ScriptPoolSpan = 0.025;

    // Clitic stem band: below a real hit, above naturalness; never settles line direction.
    private const double StemHitScore = 0.88;

    private const double StemHitSpan = 0.02;

    public bool HasDictionaries => spell?.HasDictionaries ?? false;

    public bool CanSpellFix => spell is not null;

    public IReadOnlyList<string> LanguagesFor(Script script)
    {
        return spell?.LanguagesFor(script) ?? [];
    }

    public bool HasDictionaryFor(Script script)
    {
        return spell?.Has(script) ?? false;
    }

    public double FrequencyOf(string word, Script script, string? languageCode = null)
    {
        var core = WordScanner.TrimToLetters(word, script);
        return core.Length == 0 ? 0.0 : spell?.Frequency(core, script, languageCode) ?? 0.0;
    }

    // On the frequency list or a known stem of one. A form only Hunspell affixes build does not count:
    // the Georgian affixes accept transliterated English such as ტჰატ.
    public bool IsListedWord(string word, Script script)
    {
        var core = WordScanner.TrimToLetters(word, script);
        return core.Length > 0 && spell is not null &&
               (spell.IsKnownWord(core, script) || spell.IsKnownStem(core, script, null, out _));
    }

    public bool IsLexiconForm(string word, Script script)
    {
        var core = WordScanner.TrimToLetters(word, script);
        return core.Length > 0 && spell is not null && spell.IsLexiconWord(core, script);
    }

    public static bool IsDictionaryHit(double score)
    {
        return score >= ScriptPoolHitScore;
    }

    public static bool IsWeakDictionaryHit(double score)
    {
        return score >= StemHitScore && score < ScriptPoolHitScore;
    }

    public double Score(string word, Script script, string? languageCode = null)
    {
        var core = WordScanner.TrimToLetters(word, script);
        return core.Length == 0
            ? NaturalnessScorer.Score(word, script)
            : ScoreWord(core, script, languageCode);
    }

    public double LogCountOf(string word, Script script, string? languageCode = null)
    {
        var core = WordScanner.TrimToLetters(word, script);
        return core.Length == 0 ? 0.0 : spell?.LogCount(core, script, languageCode) ?? 0.0;
    }

    public (string Text, int Edits) SpellFix(string word, Script script, string? languageCode = null)
    {
        if (!CanSpellFixWord(word, script, languageCode))
            return (word, 0);

        return spell!.TryCorrect(word, script, out var corrected, out var distance, languageCode)
               && distance > 0
               && corrected != word
            ? (corrected, distance)
            : (word, 0);
    }

    // Every spelling SpellFix would weigh rather than just its pick; none for a word the list or Hunspell
    // accepts, as with SpellFix.
    public IReadOnlyList<(string Word, int Edits)> SpellSuggestions(string word, Script script, int max)
    {
        return CanSpellFixWord(word, script, null) ? spell!.Suggestions(word, script, max) : [];
    }

    // Lexicon word is never spell-fixed into a commoner neighbour.
    private bool CanSpellFixWord(string word, Script script, string? languageCode)
    {
        return spell is not null && word.Length >= 2 && WordScanner.IsAllLetters(word, script) &&
               !HasInternalMark(word, script) && !spell.IsKnownWord(word, script, languageCode) &&
               !spell.IsLexiconWord(word, script, languageCode);
    }

    private static bool HasInternalMark(string word, Script script)
    {
        foreach (var ch in word)
            if (Scripts.IsWordInternalMark(script, ch))
                return true;

        return false;
    }

    private double ScoreWord(string word, Script script, string? languageCode)
    {
        if (spell is null)
            return NaturalnessScorer.Score(word, script) * UnknownWordPenalty;

        if (languageCode is not null)
        {
            if (spell.IsKnownInLanguage(word, languageCode))
                return OwnLanguageFloor + FrequencySpan * spell.FrequencyInLanguage(word, languageCode);
            if (spell.IsKnownWord(word, script))
                return ScriptPoolHitScore + ScriptPoolSpan * spell.Frequency(word, script);
        }
        else if (spell.IsKnownWord(word, script))
        {
            return OwnLanguageFloor + FrequencySpan * spell.Frequency(word, script);
        }

        // Lexicon hit = rarest list word, own language only — never a weaker band.
        if (spell.IsLexiconWord(word, script, languageCode))
            return OwnLanguageFloor;

        if (spell.IsKnownStem(word, script, languageCode, out var stemFrequency))
            return StemHitScore + StemHitSpan * stemFrequency;

        return Naturalness(word, script, languageCode) * UnknownWordPenalty;
    }

    public double NaturalnessOf(string word, Script script, string? languageCode = null)
    {
        if (spell is null || !spell.HasNgrams(script))
            return 0.0;

        // Drop internal marks for trigrams; the model never saw enough of them.
        var core = WithoutInternalMarks(WordScanner.TrimToLetters(word, script), script);
        return core.Length == 0 || !Orthography.IsPossibleWord(core, script)
            ? 0.0
            : spell.Naturalness(core, script, languageCode);
    }

    public double? LanguageFit(string word, Script script, string? languageCode)
    {
        if (spell is null || languageCode is null)
            return null;

        var core = WithoutInternalMarks(WordScanner.TrimToLetters(word, script), script);
        return core.Length == 0 ? null : spell.LanguageFit(core, script, languageCode);
    }

    private static string WithoutInternalMarks(string word, Script script)
    {
        System.Text.StringBuilder? sb = null;
        for (var i = 0; i < word.Length; i++)
        {
            if (!Scripts.IsWordInternalMark(script, word[i]))
            {
                sb?.Append(word[i]);
                continue;
            }

            sb ??= new System.Text.StringBuilder(word, 0, i, word.Length);
        }

        return sb?.ToString() ?? word;
    }

    private double Naturalness(string word, Script script, string? languageCode)
    {
        if (spell is null || !spell.HasNgrams(script))
            return NaturalnessScorer.Score(word, script);

        var natural = spell.Naturalness(word, script, languageCode);
        return Orthography.IsPossibleWord(word, script)
            ? natural
            : Math.Min(natural, NaturalnessScorer.ImpossibleScore);
    }
}
