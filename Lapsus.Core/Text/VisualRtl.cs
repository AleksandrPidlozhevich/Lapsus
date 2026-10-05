using Lapsus.Core.Layout;
using System.Globalization;
using System.Text;

namespace Lapsus.Core.Text;

public static class VisualRtl
{
    private static readonly Dictionary<char, char> Mirrored = new()
    {
        ['('] = ')',
        [')'] = '(',
        ['['] = ']',
        [']'] = '[',
        ['{'] = '}',
        ['}'] = '{',
        ['<'] = '>',
        ['>'] = '<',
        ['«'] = '»',
        ['»'] = '«',
        ['‹'] = '›',
        ['›'] = '‹'
    };

    public static string Flip(string text)
    {
        if (!text.Any(ch => Scripts.Of(ch) is Script.Hebrew or Script.Arabic))
            return text;

        var result = new StringBuilder(text.Length);
        var lineStart = 0;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] is not ('\n' or '\r'))
                continue;

            FlipLine(text.AsSpan(lineStart, i - lineStart), result);
            if (i < text.Length)
                result.Append(text[i]);

            lineStart = i + 1;
        }

        return result.ToString();
    }

    private static void FlipLine(ReadOnlySpan<char> line, StringBuilder result)
    {
        var start = 0;
        var end = line.Length;
        while (start < end && char.IsWhiteSpace(line[start])) start++;
        while (end > start && char.IsWhiteSpace(line[end - 1])) end--;

        result.Append(line[..start]);
        AppendReversed(line[start..end], result);
        result.Append(line[end..]);
    }

    private static void AppendReversed(ReadOnlySpan<char> core, StringBuilder result)
    {
        if (core.IsEmpty)
            return;

        var units = new List<Range>();
        var enumerator = StringInfo.GetTextElementEnumerator(core.ToString());
        while (enumerator.MoveNext())
        {
            var at = enumerator.ElementIndex;
            var length = ((string)enumerator.Current).Length;
            var cluster = core.Slice(at, length);

            if (units.Count > 0 && IsLeftToRight(cluster) && IsLeftToRight(core[units[^1]]))
                units[^1] = units[^1].Start..(at + length);
            else
                units.Add(at..(at + length));
        }

        for (var i = units.Count - 1; i >= 0; i--)
        {
            var unit = core[units[i]];
            if (unit.Length == 1 && Mirrored.TryGetValue(unit[0], out var mirror))
                result.Append(mirror);
            else
                result.Append(unit);
        }
    }

    private static bool IsLeftToRight(ReadOnlySpan<char> cluster)
    {
        var first = cluster[0];
        if (char.IsAsciiDigit(first))
            return true;

        return Scripts.Of(first) is { } script && script is not (Script.Hebrew or Script.Arabic);
    }
}
