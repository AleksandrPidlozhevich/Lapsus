namespace Lapsus.Core.Correction;

// A line cut at whitespace into chunks (words with their punctuation), and put back together.
internal static class PhraseChunks
{
    public static List<(int Start, int Length)> Of(string text)
    {
        var chunks = new List<(int Start, int Length)>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;

            if (i > start)
                chunks.Add((start, i - start));
        }

        return chunks;
    }

    // The chunks of a line as words, or null when it has a different number of them.
    public static string[]? Words(string? advice, int count)
    {
        if (advice is null)
            return null;

        var chunks = Of(advice);
        return chunks.Count == count ? chunks.Select(c => advice.Substring(c.Start, c.Length)).ToArray() : null;
    }

    public static string Separator(string text, List<(int Start, int Length)> chunks, int i)
    {
        var from = chunks[i].Start + chunks[i].Length;
        return text[from..chunks[i + 1].Start];
    }

    public static string Join(string text, List<(int Start, int Length)> chunks, string[] words, int from, int to)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = from; i < to; i++)
        {
            sb.Append(words[i]);
            if (i + 1 < chunks.Count)
                sb.Append(Separator(text, chunks, i));
        }

        return sb.ToString();
    }

    public static string Replace(string text, List<(int Start, int Length)> chunks, string[] words)
    {
        var sb = new System.Text.StringBuilder(text.Length + 8);
        var at = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            sb.Append(text, at, chunks[i].Start - at).Append(words[i]);
            at = chunks[i].Start + chunks[i].Length;
        }

        return sb.Append(text, at, text.Length - at).ToString();
    }
}
