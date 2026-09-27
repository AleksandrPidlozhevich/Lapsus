using System.Text;

namespace Lapsus.Core.Layout;

public static class LayoutTranscoder
{
    public static string Transcode(string text, KeyboardMap from, KeyboardMap to)
    {
        return string.IsNullOrEmpty(text) ? text : TranscodeKeys(text, from, null, to);
    }

    public static string Transcode(string text, IReadOnlyList<KeyboardMap> from, KeyboardMap to)
    {
        if (string.IsNullOrEmpty(text) || from.Count == 0)
            return text;

        return from.Count == 1 ? TranscodeKeys(text, from[0], null, to) : TranscodeKeys(text, null, from, to);
    }

    private static string TranscodeKeys(string text, KeyboardMap? single, IReadOnlyList<KeyboardMap>? many, KeyboardMap to)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (to.HasDeadKeys && i + 1 < text.Length &&
                FindKey(ch, single, many, out _, out var deadSlot, out var deadShift) &&
                to.IsDeadKey(deadSlot, deadShift) &&
                FindKey(text[i + 1], single, many, out _, out var baseSlot, out var baseShift) &&
                to.CharAtSlot(baseSlot, baseShift) is var baseOnTarget and not '\0' &&
                to.TryCompose(deadSlot, deadShift, baseOnTarget, out var composed))
            {
                sb.Append(composed);
                i++;
                continue;
            }

            if (!FindKey(ch, single, many, out _, out _, out _) &&
                FindDecomposition(ch, single, many, out var source, out var sourceDead, out var sourceDeadShift, out var letter) &&
                source.TryGetKey(letter, out var letterSlot, out var letterShift))
            {
                var deadOut = CharForDeadKey(to, sourceDead, sourceDeadShift);
                var letterOut = to.CharAtSlot(letterSlot, letterShift);
                if (deadOut != '\0' && letterOut != '\0')
                {
                    if (to.IsDeadKey(sourceDead, sourceDeadShift) &&
                        to.TryCompose(sourceDead, sourceDeadShift, letterOut, out var again))
                        sb.Append(again);
                    else
                        sb.Append(deadOut).Append(letterOut);

                    continue;
                }
            }

            // Ligature slots are empty; without this, MapChar leaves a Latin letter inside Arabic.
            if (to.HasLigatures &&
                FindKey(ch, single, many, out _, out var ligatureSlot, out var ligatureShift) &&
                to.LigatureAtSlot(ligatureSlot, ligatureShift) is { } ligature)
            {
                sb.Append(ligature);
                continue;
            }

            sb.Append(MapChar(ch, single, many, to));
        }

        return sb.ToString();
    }

    private static char MapChar(char ch, KeyboardMap? single, IReadOnlyList<KeyboardMap>? many, KeyboardMap to)
    {
        if (single is not null)
        {
            if (single.TryGetKey(ch, out var slot, out var shift) && to.CharAtSlot(slot, shift) is var mapped and not '\0')
                return mapped;

            return ch;
        }

        foreach (var map in many!)
        {
            if (!map.TryGetKey(ch, out var slot, out var shift))
                continue;

            if (to.CharAtSlot(slot, shift) is var mapped and not '\0')
                return mapped;
        }

        return ch;
    }

    private static bool FindKey(
        char ch, KeyboardMap? single, IReadOnlyList<KeyboardMap>? many,
        out KeyboardMap map, out int slot, out KeyShift shift)
    {
        if (single is not null)
        {
            map = single;
            return single.TryGetKey(ch, out slot, out shift);
        }

        foreach (var candidate in many!)
            if (candidate.TryGetKey(ch, out slot, out shift))
            {
                map = candidate;
                return true;
            }

        map = null!;
        slot = 0;
        shift = KeyShift.None;
        return false;
    }

    private static bool FindDecomposition(
        char ch, KeyboardMap? single, IReadOnlyList<KeyboardMap>? many,
        out KeyboardMap map, out int deadSlot, out KeyShift deadShift, out char letter)
    {
        if (single is not null)
        {
            map = single;
            return single.TryDecompose(ch, out deadSlot, out deadShift, out letter);
        }

        foreach (var candidate in many!)
            if (candidate.TryDecompose(ch, out deadSlot, out deadShift, out letter))
            {
                map = candidate;
                return true;
            }

        map = null!;
        deadSlot = 0;
        deadShift = KeyShift.None;
        letter = '\0';
        return false;
    }

    private static char CharForDeadKey(KeyboardMap to, int slot, KeyShift shift)
    {
        if (shift == KeyShift.None)
            return to.CharAtSlot(slot);

        var declared = to.DeclaredShiftedChar(slot);
        return declared != '\0' ? declared : to.CharAtSlot(slot, KeyShift.Case);
    }

    public static string TranscodeIntoScript(
        string text, IReadOnlyList<KeyboardMap> from, KeyboardMap to, Script toScript)
    {
        if (string.IsNullOrEmpty(text) || !Scripts.IsMixed(text))
            return Transcode(text, from, to);

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;

            if (i > start)
            {
                var run = text[start..i];
                sb.Append(Scripts.Dominant(run) == toScript ? run : Transcode(run, from, to));
            }

            while (i < text.Length && char.IsWhiteSpace(text[i]))
                sb.Append(text[i++]);
        }

        return sb.ToString();
    }

    public static string TranscodeIntoScript(
        string text, IReadOnlyList<KeyboardMap> from, KeyboardMap to, Script toScript,
        IReadOnlyList<TextSpan> keep)
    {
        if (keep.Count == 0)
            return TranscodeIntoScript(text, from, to, toScript);

        if (string.IsNullOrEmpty(text))
            return text;

        var mask = TextSpans.BuildMask(text.Length, keep);

        var spareByScript = Scripts.IsMixed(text);
        var sb = new StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;

            if (i > start)
                AppendKeeping(sb, text, start, i, mask, from, to, toScript, spareByScript);

            while (i < text.Length && char.IsWhiteSpace(text[i]))
                sb.Append(text[i++]);
        }

        return sb.ToString();
    }

    private static void AppendKeeping(
        StringBuilder sb, string text, int start, int end, bool[] mask,
        IReadOnlyList<KeyboardMap> from, KeyboardMap to, Script toScript, bool spareByScript)
    {
        var run = text[start..end];
        if (spareByScript && Scripts.Dominant(run) == toScript)
        {
            sb.Append(run);
            return;
        }

        var i = start;
        while (i < end)
        {
            var segmentStart = i;
            var kept = mask[i];
            while (i < end && mask[i] == kept)
                i++;

            var segment = text[segmentStart..i];
            sb.Append(kept ? segment : Transcode(segment, from, to));
        }
    }

    public static bool HasMistypedLetterKey(string text, Script script, KeyboardMap from, KeyboardMap to)
    {
        foreach (var ch in text)
        {
            if (Alphabets.IsLetterOf(script, ch))
                continue;

            if (from.TryGetSlot(char.ToLowerInvariant(ch), out var slot)
                && Alphabets.IsLetterOf(script, to.CharAtSlot(slot)))
                return true;
        }

        return false;
    }
}
