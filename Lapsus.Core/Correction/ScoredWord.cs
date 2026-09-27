using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

internal readonly record struct ScoredWord(
    string Text,
    KeyboardLayout? Target,
    string? TargetLayoutId,
    double Score,
    bool Changed,
    int Edits = 0)
{
    public static ScoredWord AsTyped(string word, double score)
    {
        return new ScoredWord(word, null, null, score, false);
    }
}
