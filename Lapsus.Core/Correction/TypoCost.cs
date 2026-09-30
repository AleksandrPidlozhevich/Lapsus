using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

// How likely a slip of the fingers turns the meant word into the typed one, as an edit distance whose
// steps are priced by the keyboard: hitting the key next door, swapping two letters or dropping one is
// cheap; a letter from across the board is not. SymSpell calls "мауть" one edit from both "мабуть" and
// "мають", but only the first is a dropped key — у and ю are a row and eight keys apart.
public static class TypoCost
{
    internal const double NeighbourSubstitution = 0.6;

    internal const double FarSubstitution = 1.4;

    internal const double Transposition = 0.7;

    // The meant word has a letter the typed one lacks.
    internal const double Dropped = 0.8;

    // The typed word has a letter too many: a neighbour caught with the right key, or one key held too long.
    internal const double ExtraNeighbour = 0.6;

    internal const double Extra = 1.2;

    public static double Between(string typed, string meant, KeyboardMap map)
    {
        var a = typed.ToLowerInvariant();
        var b = meant.ToLowerInvariant();
        var d = new double[a.Length + 1, b.Length + 1];

        for (var i = 0; i <= a.Length; i++)
            d[i, 0] = i == 0 ? 0 : d[i - 1, 0] + ExtraCost(a, i - 1, map);
        for (var j = 1; j <= b.Length; j++)
            d[0, j] = d[0, j - 1] + Dropped;

        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = a[i - 1] == b[j - 1]
                    ? 0.0
                    : KeyNeighbours.AreNeighbours(a[i - 1], b[j - 1], map) ? NeighbourSubstitution : FarSubstitution;

                var best = Math.Min(d[i - 1, j - 1] + substitution,
                    Math.Min(d[i - 1, j] + ExtraCost(a, i - 1, map), d[i, j - 1] + Dropped));

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1] && a[i - 1] != a[i - 2])
                    best = Math.Min(best, d[i - 2, j - 2] + Transposition);

                d[i, j] = best;
            }

        return d[a.Length, b.Length];
    }

    // A letter typed but not meant costs little beside a key it neighbours or repeats.
    private static double ExtraCost(string typed, int at, KeyboardMap map)
    {
        var ch = typed[at];
        var before = at > 0 ? typed[at - 1] : '\0';
        var after = at + 1 < typed.Length ? typed[at + 1] : '\0';
        return ch == before || KeyNeighbours.AreNeighbours(ch, before, map) || KeyNeighbours.AreNeighbours(ch, after, map)
            ? ExtraNeighbour
            : Extra;
    }
}
