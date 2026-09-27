using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

internal readonly record struct TokenReading(
    string Typed,
    WordSource Source,
    double Baseline,
    double BaselineFrequency,
    ScoredWord Chosen,
    Script? ChosenScript,
    bool IsWord,
    bool IsException = false)
{
    public int Edits => Chosen.Edits;

    public static TokenReading Verbatim(string text)
    {
        return new TokenReading(text, default, 0.0, 0.0, ScoredWord.AsTyped(text, 0.0), null, false);
    }

    public TokenReading Reverted()
    {
        return this with { Chosen = ScoredWord.AsTyped(Typed, Baseline), ChosenScript = Source.Script };
    }
}
