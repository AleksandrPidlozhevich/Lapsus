using System.Text;

namespace Lapsus.Core.Layout;

public readonly record struct DeadKey(int Slot, bool Shifted, char Accent);

public readonly record struct Ligature(int Slot, bool Shifted, string Text);

public static class DeadKeys
{
    public static string? CombiningMarks(char accent)
    {
        return accent switch
        {
            '΄' or '´' or 'ˊ' => "́",
            '¨' => "̈",
            '΅' => "̈́",
            '`' or 'ˋ' => "̀",
            '^' or 'ˆ' => "̂",
            '~' or '˜' => "̃",
            'ˇ' => "̌",
            '˘' => "̆",
            '˚' => "̊",
            '˝' => "̋",
            '¸' => "̧",
            '˛' => "̨",
            '˙' => "̇",
            _ => null
        };
    }

    public static char? Compose(char letter, string marks)
    {
        try
        {
            var composed = (letter + marks).Normalize(NormalizationForm.FormC);
            return composed.Length == 1 && composed[0] != letter ? composed[0] : null;
        }
        catch (PlatformNotSupportedException)
        {
            // Invariant globalization: no compositions rather than a crash on the hook.
            return null;
        }
    }
}
