namespace Lapsus.Core.Layout;

public readonly record struct TextSpan(int Start, int Length, bool Absolute = false);

public static class TextSpans
{
    public static bool[] BuildMask(int length, IReadOnlyList<TextSpan> spans)
    {
        var mask = new bool[length];
        foreach (var span in spans)
        {
            var start = Math.Max(0, span.Start);
            var end = Math.Min(length, span.Start + Math.Max(0, span.Length));
            for (var i = start; i < end; i++)
                mask[i] = true;
        }

        return mask;
    }

    public static bool CoversEveryLetter(string text, IReadOnlyList<TextSpan> spans)
    {
        var mask = BuildMask(text.Length, spans);
        var sawLetter = false;

        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsLetter(text[i]))
                continue;

            if (!mask[i])
                return false;

            sawLetter = true;
        }

        return sawLetter;
    }
}
