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
