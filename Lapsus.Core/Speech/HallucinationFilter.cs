using System.Text;
using System.Text.RegularExpressions;

namespace Lapsus.Core.Speech;

// Cleans what Whisper invents rather than hears. Two kinds, both seen in stage 1:
//  - subtitle credits over silence ("Субтитры создавал DimaTorzok", "Продолжение следует...");
//  - loops, where the decoder repeats a sentence until it runs out of room, often ending on a cut-off copy.
// Silence itself is cut before decoding (VAD); this is the second line.
public static partial class HallucinationFilter
{
    private static readonly Lazy<(HashSet<string> Exact, string[] Contains)> Phrases = new(Load);

    public static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var sentences = Sentences(text.Trim());
        var kept = new List<string>();
        foreach (var sentence in sentences)
        {
            if (IsHallucination(sentence))
                continue;
            // A sentence said twice in a row is a loop, not emphasis; the speaker would not dictate it so.
            if (kept.Count > 0 && Normalize(kept[^1]) == Normalize(sentence))
                continue;
            kept.Add(sentence);
        }

        DropCutOffRepeat(kept);
        var joined = CollapseRepeatedRuns(string.Join(' ', kept));
        return joined.Trim();
    }

    public static bool IsHallucination(string sentence)
    {
        var normalized = Normalize(sentence);
        if (normalized.Length == 0)
            return true;

        var (exact, contains) = Phrases.Value;
        if (exact.Contains(normalized))
            return true;

        foreach (var marker in contains)
            if (normalized.Contains(marker, StringComparison.Ordinal))
                return true;

        return false;
    }

    // A loop that ran out of tokens ends on a partial copy of an earlier sentence: drop it.
    private static void DropCutOffRepeat(List<string> sentences)
    {
        if (sentences.Count < 2)
            return;

        var last = Normalize(sentences[^1]);
        if (last.Split(' ').Length < 2)
            return;

        for (var i = 0; i < sentences.Count - 1; i++)
        {
            var earlier = Normalize(sentences[i]);
            if (earlier.Length > last.Length && earlier.StartsWith(last, StringComparison.Ordinal))
            {
                sentences.RemoveAt(sentences.Count - 1);
                return;
            }
        }
    }

    // A run of 1–10 words repeated back to back: three times for a phrase, four for a single word ("нет, нет,
    // нет" is speech; a fourth is a loop). Keeps one copy.
    private static string CollapseRepeatedRuns(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var keys = words.Select(Normalize).ToList();

        for (var n = 10; n >= 1; n--)
        {
            var minRepeats = n == 1 ? 4 : 3;
            for (var start = 0; start + n * minRepeats <= words.Count; start++)
            {
                var repeats = 1;
                while (start + (repeats + 1) * n <= words.Count && SameRun(keys, start, start + repeats * n, n))
                    repeats++;

                if (repeats < minRepeats || keys.Skip(start).Take(n).All(k => k.Length == 0))
                    continue;

                words.RemoveRange(start + n, (repeats - 1) * n);
                keys.RemoveRange(start + n, (repeats - 1) * n);
            }
        }

        return string.Join(' ', words);
    }

    private static bool SameRun(List<string> keys, int a, int b, int n)
    {
        for (var i = 0; i < n; i++)
            if (keys[a + i] != keys[b + i])
                return false;
        return true;
    }

    private static List<string> Sentences(string text)
    {
        return SentenceEnd().Split(text).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }

    // Lower case, letters and digits only, single spaces.
    internal static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (space && sb.Length > 0)
                    sb.Append(' ');
                sb.Append(ch == 'ё' ? 'е' : ch);
                space = false;
            }
            else
            {
                space = true;
            }
        }
        return sb.ToString();
    }

    private static (HashSet<string>, string[]) Load()
    {
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var contains = new List<string>();
        foreach (var line in SpeechData.Lines("hallucinations.txt"))
        {
            if (line.StartsWith('~'))
                contains.Add(Normalize(line[1..]));
            else
                exact.Add(Normalize(line));
        }
        return (exact, contains.Where(c => c.Length > 0).ToArray());
    }

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceEnd();
}

internal static class SpeechData
{
    // Non-empty lines of an embedded Speech/Data file, without # comments.
    public static IEnumerable<string> Lines(string name)
    {
        using var stream = typeof(SpeechData).Assembly.GetManifestResourceStream($"Lapsus.Core.Speech.Data.{name}")
                           ?? throw new InvalidOperationException($"missing embedded resource {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                lines.Add(trimmed);
        }
        return lines;
    }
}
