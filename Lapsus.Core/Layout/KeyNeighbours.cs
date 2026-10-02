namespace Lapsus.Core.Layout;

// Which keys sit next to which on a staggered ANSI board, read through any layout: a slip lands on a
// neighbour far more often than anywhere else, whatever the key prints.
public static class KeyNeighbours
{
    // The three letter rows as the US layout prints them, with each row's stagger in key widths.
    private static readonly (string Keys, double Offset)[] Rows =
    [
        ("qwertyuiop[]", 0.0),
        ("asdfghjkl;'", 0.25),
        ("zxcvbnm,./", 0.75)
    ];

    public static bool AreNeighbours(char a, char b, KeyboardMap map)
    {
        return Position(a, map) is { } pa && Position(b, map) is { } pb &&
               pa != pb && Math.Abs(pa.Row - pb.Row) <= 1 && Math.Abs(pa.X - pb.X) <= 1.0;
    }

    public static IReadOnlyList<char> Of(char ch, KeyboardMap map)
    {
        if (Position(ch, map) is not { } at)
            return [];

        var found = new List<char>();
        for (var row = Math.Max(at.Row - 1, 0); row <= Math.Min(at.Row + 1, Rows.Length - 1); row++)
        {
            var (keys, offset) = Rows[row];
            for (var i = 0; i < keys.Length; i++)
            {
                if ((row == at.Row && i + offset == at.X) || Math.Abs(i + offset - at.X) > 1.0)
                    continue;

                if (BundledKeyboardMaps.En.TryGetSlot(keys[i], out var slot) &&
                    map.CharAtSlot(slot) is var printed && char.IsLetter(printed))
                    found.Add(char.IsUpper(ch) ? char.ToUpperInvariant(printed) : printed);
            }
        }

        return found;
    }

    private static (int Row, double X)? Position(char ch, KeyboardMap map)
    {
        if (!map.TryGetSlot(char.ToLowerInvariant(ch), out var slot))
            return null;

        var us = BundledKeyboardMaps.En.CharAtSlot(slot);
        for (var row = 0; row < Rows.Length; row++)
        {
            var i = Rows[row].Keys.IndexOf(us);
            if (i >= 0)
                return (row, i + Rows[row].Offset);
        }

        return null;
    }
}
