using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using Lapsus.Input;
using System.Text;

namespace Lapsus.Tests;

// Each line of Fixtures/cycle.cases.tsv is typed into the backend, keystroke by keystroke on the layout it was
// typed on, and the hotkey is pressed ten times, the system layout following every switch as the OS does. Every
// screen is checked: the first press, the words typed right, and the circle the presses walk.
public sealed class HotkeyCircleBenchTests(HotkeyCircleBenchTests.Lists lists)
    : IClassFixture<HotkeyCircleBenchTests.Lists>
{
    private const int Presses = 10;

    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static readonly Language[] Known =
    [
        new("en", Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En),
        new("ru", Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru),
        new("uk", Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk),
        new("be", Script.Cyrillic, KeyboardLayout.Be, BundledKeyboardMaps.Be),
        new("bg", Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.Bg),
        new("el", Script.Greek, KeyboardLayout.El, BundledKeyboardMaps.El),
        new("he", Script.Hebrew, KeyboardLayout.He, BundledKeyboardMaps.He),
        new("ar", Script.Arabic, KeyboardLayout.Ar, BundledKeyboardMaps.Ar),
        new("ka", Script.Georgian, KeyboardLayout.Ka, BundledKeyboardMaps.Ka),
        new("hy", Script.Armenian, KeyboardLayout.Hy, BundledKeyboardMaps.Hy)
    ];

    public static TheoryData<string, string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var line in File.ReadLines(Path.Combine(FixtureDir, "cycle.cases.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var fields = line.Split('\t');
            data.Add(fields[0], fields[1], fields[2], fields.Length > 3 ? fields[3] : string.Empty);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void First_press_gives_the_line_as_written(string layouts, string line, string status, string note)
    {
        var walk = Walk(layouts, line);
        Expect("first", status, walk.Screens[0] == walk.Line.Expected,
            $"first press: \"{walk.Screens[0]}\", wanted \"{walk.Line.Expected}\"", walk, note);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void No_press_changes_a_word_typed_right(string layouts, string line, string status, string note)
    {
        var walk = Walk(layouts, line);
        var broken = walk.Screens
            .Select((screen, i) => (Press: i + 1, Problem: walk.Line.ChangedRightWords(screen)))
            .Where(p => p.Problem is not null)
            .Select(p => $"press {p.Press}: {p.Problem}")
            .ToList();

        Expect("words", status, broken.Count == 0, string.Join("; ", broken), walk, note);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_presses_walk_one_circle_back_to_the_typed_line(
        string layouts, string line, string status, string note)
    {
        var walk = Walk(layouts, line);
        var screens = walk.Screens;
        var back = screens.IndexOf(walk.Line.Typed);

        string? problem = null;
        if (back < 0)
            problem = "never comes back to the typed line";
        else if (screens.Where((s, i) => s != screens[i % (back + 1)]).Any())
            problem = $"the presses after press {back + 1} do not repeat the circle";
        else if (!screens.Take(back + 1).Contains(walk.Line.Expected))
            problem = $"\"{walk.Line.Expected}\" is not on the circle";

        Expect("circle", status, problem is null, problem ?? string.Empty, walk, note);
    }

    // Guards the bench itself: a word typed right that no list knows would test the unknown-word path by accident.
    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_word_typed_right_is_listed(string layouts, string line, string status, string note)
    {
        var parsed = TypedLine.Parse(Find(layouts), line);
        var unlisted = parsed.RightWords
            .Where(w => !lists.Knows(w.Language.Code, w.Word.ToLowerInvariant()) &&
                        !lists.LeftOutOnPurpose(w.Language.Code, w.Word.ToLowerInvariant()))
            .Select(w => $"{w.Language.Code}:{w.Word}")
            .ToList();

        Assert.True(unlisted.Count == 0, $"{note}: add to Fixtures/cycle.extra.tsv: {string.Join(", ", unlisted)}");
    }

    private Walked Walk(string layouts, string line)
    {
        var languages = Find(layouts);
        var parsed = TypedLine.Parse(languages, line);
        var installed = languages.Select((l, i) => l.Installed(i + 1)).ToList();
        var brain = new LayoutCorrector(new SpellChecker(languages.Select(lists.Source).ToList()));

        var backend = new FakeInputBackend(brain)
        {
            InstalledLayouts = installed,
            FollowLayoutSwitches = true
        }.Listening();

        foreach (var (text, on) in parsed.Keystrokes)
        {
            var layout = installed[Array.IndexOf(languages, on)];
            backend.Active = layout;
            backend.TypeOn((long)layout.Hkl, text);
        }

        var screen = parsed.Typed;
        var screens = new List<string>();
        for (var press = 0; press < Presses; press++)
        {
            var injected = backend.Injected.Count;
            backend.PressHotkey();
            foreach (var (backspaces, text) in backend.Injected.Skip(injected))
                screen = screen[..^backspaces] + text;

            screens.Add(screen);
        }

        return new Walked(parsed, screens, backend.Diagnostics);
    }

    private static void Expect(string check, string status, bool passed, string problem, Walked walk, string note)
    {
        var failing = status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (failing.Any(f => f != "ok" && f is not ("first" or "words" or "circle")))
            throw new InvalidDataException($"Unknown status \"{status}\"; use ok or first,words,circle.");

        var report = $"{note}\n  typed:   {walk.Line.Typed}\n  written: {walk.Line.Expected}\n" +
                     string.Concat(walk.Screens.Select((s, i) => $"  press {i + 1,2}: {s}\n")) +
                     (walk.Diagnostics.Count > 0 ? $"  diagnostics: {string.Join(" | ", walk.Diagnostics)}\n" : "");

        if (failing.Contains(check))
            Assert.True(!passed, $"[{check}] now passes — drop it from the row's status.\n{report}");
        else
            Assert.True(passed, $"[{check}] {problem}\n{report}");
    }

    private static Language[] Find(string layouts)
    {
        return layouts.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(code => Known.FirstOrDefault(l => l.Code == code)
                            ?? throw new InvalidDataException($"Unknown layout \"{code}\"."))
            .ToArray();
    }

    private sealed record Walked(TypedLine Line, List<string> Screens, List<string> Diagnostics);

    public sealed record Language(string Code, Script Script, KeyboardLayout Layout, KeyboardMap Map)
    {
        public InstalledLayout Installed(int hkl)
        {
            return new InstalledLayout($"{Code}-id", hkl, Script, Code, Layout, Map);
        }

        public bool CanType(string text)
        {
            foreach (var ch in text)
                if (char.IsLetter(ch) && !Map.TryGetKey(ch, out _, out _) &&
                    !Map.TryGetKey(ch.ToString().Normalize(NormalizationForm.FormD)[0], out _, out _))
                    return false;

            return true;
        }
    }

    // A line with its markup resolved: what the screen shows, what the writer meant, the keystrokes that typed it
    // and which characters were typed right.
    private sealed class TypedLine
    {
        private bool[] _right = [];

        public string Typed { get; private set; } = "";

        public string Expected { get; private set; } = "";

        public List<(string Text, Language On)> Keystrokes { get; } = [];

        public List<(string Word, Language Language)> RightWords { get; } = [];

        public static TypedLine Parse(Language[] installed, string line)
        {
            var parsed = new TypedLine();
            var typed = new StringBuilder();
            var expected = new StringBuilder();
            var right = new List<bool>();
            var current = installed[0];

            void Type(string text, Language on, bool isRight)
            {
                parsed.Keystrokes.Add((text, on));
                typed.Append(text);
                right.AddRange(Enumerable.Repeat(isRight, text.Length));
                current = on;
            }

            for (var i = 0; i < line.Length;)
            {
                if (line[i] == '{')
                {
                    var colon = line.IndexOf(':', i);
                    var close = line.IndexOf('}', i);
                    if (colon < 0 || close < colon)
                        throw new InvalidDataException($"Unclosed markup in \"{line}\".");

                    var on = installed.FirstOrDefault(l => l.Code == line[(i + 1)..colon])
                             ?? throw new InvalidDataException($"\"{line[(i + 1)..colon]}\" is not installed: \"{line}\".");
                    var meant = line[(colon + 1)..close];
                    var own = OwnLayout(meant, installed);
                    var keys = LayoutTranscoder.Transcode(meant, own.Map, on.Map);
                    if (LayoutTranscoder.Transcode(keys, on.Map, own.Map) != meant)
                        throw new InvalidDataException($"\"{meant}\" does not type cleanly on {on.Code} keys (\"{keys}\").");

                    Type(keys, on, false);
                    expected.Append(meant);
                    i = close + 1;
                    continue;
                }

                // Text typed right: each word on its own layout, spaces and marks on whatever is on.
                var end = line.IndexOf('{', i);
                if (end < 0)
                    end = line.Length;

                foreach (var piece in Pieces(line[i..end]))
                {
                    // The word without the marks glued to its ends; a hyphen inside stays ("что-то").
                    var word = piece.Trim().TrimStart(NotLetters(piece)).TrimEnd(NotLetters(piece));
                    var on = word.Length > 0 ? OwnLayout(word, installed) : current;
                    if (word.Length > 0)
                        parsed.RightWords.Add((word, on));

                    Type(piece, on, true);
                    expected.Append(piece);
                }

                i = end;
            }

            parsed.Typed = typed.ToString();
            parsed.Expected = expected.ToString();
            parsed._right = [.. right];
            return parsed;
        }

        // Null when every word typed right is still on the screen, word for word.
        public string? ChangedRightWords(string screen)
        {
            var before = Tokens(Typed);
            var after = Tokens(screen);
            if (before.Count != after.Count)
                return $"the line went from {before.Count} words to {after.Count}";

            var changed = new List<string>();
            for (var w = 0; w < before.Count; w++)
            {
                var (start, text) = before[w];
                var typedRight = Enumerable.Range(start, text.Length).All(c => _right[c]) &&
                                 text.Any(char.IsLetter);
                if (typedRight && text != after[w].Text)
                    changed.Add($"\"{text}\" → \"{after[w].Text}\"");
            }

            return changed.Count == 0 ? null : string.Join(", ", changed);
        }

        private static List<(int Start, string Text)> Tokens(string text)
        {
            var tokens = new List<(int, string)>();
            for (var i = 0; i < text.Length;)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    i++;
                    continue;
                }

                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;
                tokens.Add((start, text[start..i]));
            }

            return tokens;
        }

        // Words with the marks glued to them, and the spaces between, in order.
        private static IEnumerable<string> Pieces(string text)
        {
            for (var i = 0; i < text.Length;)
            {
                var space = char.IsWhiteSpace(text[i]);
                var start = i;
                while (i < text.Length && char.IsWhiteSpace(text[i]) == space)
                    i++;
                yield return text[start..i];
            }
        }

        private static char[] NotLetters(string text)
        {
            return text.Where(c => !char.IsLetter(c)).Distinct().ToArray();
        }

        private static Language OwnLayout(string text, Language[] installed)
        {
            var script = Scripts.Dominant(text)
                         ?? throw new InvalidDataException($"\"{text}\" has no letters to tell its layout by.");
            return installed.FirstOrDefault(l => l.Script == script && l.CanType(text))
                   ?? throw new InvalidDataException($"No installed layout types \"{text}\".");
        }
    }

    // The fixture word lists, each with the bench's extra words added at their real counts.
    public sealed class Lists : IDisposable
    {
        private readonly Dictionary<string, (HashSet<string> Words, DictionarySource Source)> _byCode = [];

        private readonly HashSet<(string Code, string Word)> _leftOut;

        public Lists()
        {
            var extra = File.ReadLines(Path.Combine(FixtureDir, "cycle.extra.tsv"))
                .Where(l => l.Length > 0 && l[0] != '#')
                .Select(l => l.Split('\t'))
                .ToList();
            var added = extra.Where(f => f[2] != "-").ToLookup(f => f[0], f => $"{f[1]} {f[2]}");
            _leftOut = extra.Where(f => f[2] == "-").Select(f => (f[0], f[1])).ToHashSet();

            foreach (var language in Known)
            {
                var lines = File.ReadLines(Path.Combine(FixtureDir, $"{language.Code}.words.txt"))
                    .Concat(added[language.Code]).ToList();
                var path = Path.Combine(Path.GetTempPath(), $"lapsus-bench-{language.Code}-{Guid.NewGuid():N}.txt");
                File.WriteAllLines(path, lines);

                var words = lines.Select(l => l.Split(' ')[0]).ToHashSet(StringComparer.Ordinal);
                _byCode[language.Code] = (words, new DictionarySource(language.Code, path, language.Script));
            }
        }

        public bool Knows(string code, string word)
        {
            return _byCode[code].Words.Contains(word);
        }

        public bool LeftOutOnPurpose(string code, string word)
        {
            return _leftOut.Contains((code, word));
        }

        public DictionarySource Source(Language language)
        {
            return _byCode[language.Code].Source;
        }

        public void Dispose()
        {
            foreach (var (_, source) in _byCode.Values)
                File.Delete(source.Path);
        }
    }
}
