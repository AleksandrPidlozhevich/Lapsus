using System.Text;
using System.Text.RegularExpressions;

namespace Lapsus.Core.Speech;

// Drops hesitation fillers Whisper and Parakeet write as words ("э", "эм", "uh", "um"). Off by default: a real
// "э" said as the letter name would go too. The list is in Data/fillers.txt.
public static partial class FillerWords
{
    private static readonly Lazy<HashSet<string>> Tokens = new(Load);

    public static string Remove(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var fillers = Tokens.Value;
        var kept = new StringBuilder(text.Length);
        var lastWasSpace = true;
        foreach (Match m in Token().Matches(text))
        {
            if (m.Groups["word"].Success)
            {
                var word = m.Groups["word"].Value;
                if (fillers.Contains(word.ToLowerInvariant()))
                    continue;
                if (kept.Length > 0 && !lastWasSpace && !IsClosingPunctuation(word[0]))
                    kept.Append(' ');
                kept.Append(word);
                lastWasSpace = false;
            }
            else if (m.Groups["space"].Success)
            {
                if (kept.Length == 0 || lastWasSpace)
                    continue;
                kept.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                var mark = m.Value;
                // A comma that only hung off a dropped filler ("э, привет") has nothing to attach to.
                if (kept.Length == 0 && IsClosingPunctuation(mark[0]))
                    continue;
                if (kept.Length > 0 && lastWasSpace && IsClosingPunctuation(mark[0]))
                    kept.Length--;
                kept.Append(mark);
                lastWasSpace = false;
            }
        }

        return kept.ToString().Trim();
    }

    private static bool IsClosingPunctuation(char c) =>
        c is ',' or '.' or '!' or '?' or ';' or ':' or '…' or ')' or ']' or '}' or '»' or '"';

    private static HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in SpeechData.Lines("fillers.txt"))
            set.Add(line.ToLowerInvariant());
        return set;
    }

    // word | runs of spaces | everything else (punctuation kept as-is).
    [GeneratedRegex(@"(?<word>[\p{L}\p{N}']+)|(?<space>\s+)|.", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
