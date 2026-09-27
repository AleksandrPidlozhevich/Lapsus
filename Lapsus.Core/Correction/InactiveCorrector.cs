using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public sealed class InactiveCorrector(bool preferAsync = false) : IPhraseCorrector
{
    public bool IsReady => false;

    public bool SupportsLayoutCycle => false;

    public bool PreferAsync { get; } = preferAsync;

    public PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred = null)
    {
        return new PhraseCorrection(text, text, false, null);
    }
}
