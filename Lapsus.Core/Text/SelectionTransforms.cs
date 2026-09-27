using Lapsus.Core.Layout;

namespace Lapsus.Core.Text;

public enum SelectionAction
{
    Correct = 0,

    CycleCase = 1,

    Transliterate = 2,

    ReverseRtl = 3
}

public static class SelectionTransforms
{
    public static string Apply(
        SelectionAction action, string text, Script latinTarget = Script.Cyrillic, string? language = null)
    {
        return action switch
        {
            SelectionAction.CycleCase => CaseCycle.Next(text),
            SelectionAction.Transliterate => Transliterator.Convert(text, latinTarget, language),
            SelectionAction.ReverseRtl => VisualRtl.Flip(text),
            _ => text
        };
    }
}
