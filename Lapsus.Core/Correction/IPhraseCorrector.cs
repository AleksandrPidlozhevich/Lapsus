using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public interface IPhraseCorrector
{
    bool IsReady { get; }

    bool SupportsLayoutCycle { get; }

    bool PreferAsync { get; }

    bool SupportsAutoMode
    {
        get { return false; }
    }

    bool Knows(Script script)
    {
        return false;
    }

    // Only meaningful for a script Knows answers true for.
    bool KnowsWord(string word, Script script)
    {
        return false;
    }

    // Known, or a form the Hunspell affixes build ("вихідними" off a short Ukrainian list). Too loose to
    // vouch for a word moved in from another script — Georgian affixes accept "ტჰატ" — but enough to keep
    // a word as typed.
    bool AcceptsSpelling(string word, Script script)
    {
        return KnowsWord(word, script);
    }

    // Spellings a mistyped word may have meant, closest first; none for a word the dictionary knows.
    IReadOnlyList<(string Word, int Edits)> SpellSuggestions(string word, Script script)
    {
        return [];
    }

    PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred = null);

    PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred,
        CorrectionHints hints)
    {
        return CorrectPhrase(text, active, installed, candidates, preferred);
    }
}
