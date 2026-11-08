using System.Text.RegularExpressions;

namespace Lapsus.Core.Speech;

// "Как дела вопросительный знак" → "Как дела?". Whisper writes the names of punctuation marks as words, and
// often wraps them in punctuation of its own ("Привет. Запятая. Как дела"), which goes with the word.
public static class SpokenPunctuation
{
    private const string Marks = ",.;:!?…";

    private static readonly Lazy<Dictionary<string, List<(Regex Pattern, string Mark, bool Anywhere)>>> Commands =
        new(Load);

    public static string Apply(string text, string language)
    {
        if (!Commands.Value.TryGetValue(DictationLanguagePicker.Normalize(language), out var commands))
            return text;

        foreach (var (pattern, mark, anywhere) in commands)
        {
            var from = 0;
            while (from < text.Length && pattern.Match(text, from) is { Success: true } m)
            {
                var lead = m.Groups["lead"].Value;
                var trail = m.Groups["trail"].Value;
                var rest = text[(m.Index + m.Length)..];
                // shortcut: a word said at a sentence's end without Whisper's punctuation before it ("как дела
                // точка") stays a word; loosen once real recordings show how Whisper writes these.
                var setApart = (m.Index == 0 || lead.IndexOfAny(Marks.ToCharArray()) >= 0)
                               && (trail.Length > 0 || rest.Trim().Length == 0);
                if (!anywhere && !setApart)
                {
                    from = m.Index + m.Length;
                    continue;
                }

                var head = text[..m.Index] + mark;
                text = head + FitCase(rest, mark);
                from = head.Length;
            }
        }

        return text;
    }

    // The word after a mark takes the case the mark asks for; Whisper's own capital after its own period goes.
    private static string FitCase(string rest, string mark)
    {
        var at = 0;
        while (at < rest.Length && char.IsWhiteSpace(rest[at]))
            at++;
        if (at == rest.Length || !char.IsLetter(rest[at]))
            return rest;

        var word = rest[at..];
        word = mark is "," or ":" or ";"
            ? DictationText.Decapitalize(word)
            : char.ToUpperInvariant(word[0]) + word[1..];
        return rest[..at] + word;
    }

    private static Dictionary<string, List<(Regex, string, bool)>> Load()
    {
        var commands = new Dictionary<string, List<(Regex, string, bool)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SpeechData.Lines("commands.txt"))
        {
            var parts = line.Split('\t');
            if (parts.Length != 4)
                throw new InvalidOperationException($"commands.txt: bad line \"{line}\"");

            var words = string.Join(@"\s+", parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
            var pattern = new Regex($@"(?<lead>[\s{Regex.Escape(Marks)}]*)(?<![\w]){words}(?![\w])(?<trail>[{Regex.Escape(Marks)}]*)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!commands.TryGetValue(parts[0], out var list))
                commands[parts[0]] = list = [];
            list.Add((pattern, parts[2], parts[3] == "anywhere"));
        }

        // Longer phrases first, so "знак питання" is not read as something shorter inside it.
        foreach (var list in commands.Values)
            list.Sort((a, b) => b.Item1.ToString().Length.CompareTo(a.Item1.ToString().Length));
        return commands;
    }
}
