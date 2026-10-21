using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using System.Text;

namespace Lapsus.Core.Tests.Languages;

// A writer who switches layouts word by word ("ну да, это был deploy на prod"), forgets the switch now and
// then, and presses the hotkey over the whole line. Rows live in Fixtures/mixed.cases.tsv; a pending row
// must still fail, as in TypingFixtureTests.
public sealed class MixedLayoutLineTests
{
    public static TheoryData<string, string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var line in File.ReadLines(Path.Combine(TestDictionaries.FixtureDir, "mixed.cases.tsv")))
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
    public void Hotkey_over_a_mixed_line(string layouts, string line, string status, string note)
    {
        var mixed = MixedLine.Parse(layouts, line);
        var corrector = new LayoutCorrector(new SpellChecker(mixed.Layouts
            .Select(l => l.Code).Distinct()
            .Select(code => new DictionarySource(code, TestDictionaries.Fixture(code), MixedLine.Find(code).Script))
            .ToList()));

        var got = mixed.Correct(corrector);
        var report = $"{note} [{layouts}, hotkey on {mixed.Active.Code}]: typed \"{mixed.Typed}\" → \"{got}\", " +
                     $"wanted \"{mixed.Expected}\"";

        switch (status)
        {
            case "ok":
                Assert.True(mixed.Expected == got, report);
                break;
            case "pending":
                Assert.True(mixed.Expected != got, $"{report} — now comes back right; mark the row ok");
                break;
            default:
                throw new InvalidDataException($"Unknown status \"{status}\" for \"{line}\"; use ok or pending.");
        }
    }

    [Fact]
    public void Markup_types_the_forgotten_part_on_the_layout_that_was_active()
    {
        var mixed = MixedLine.Parse("en ru", "ой, я забыл про meeting, {en:ну ладно}");

        Assert.Equal("ой, я забыл про meeting, ye kflyj", mixed.Typed);
        Assert.Equal("ой, я забыл про meeting, ну ладно", mixed.Expected);
        Assert.Equal("en", mixed.Active.Code);
    }

    [Fact]
    public void Markup_reads_cyrillic_by_the_letters_only_one_layout_has()
    {
        var mixed = MixedLine.Parse("en ru uk", "ой, це {ru:інше}");

        Assert.Equal("ой, це ынше", mixed.Typed);
        Assert.Equal("ru", mixed.Active.Code);
    }

    [Fact]
    public void Markup_types_greek_accents_as_the_dead_key_before_the_letter()
    {
        var mixed = MixedLine.Parse("en el", "εντάξει, see you {en:αύριο}");

        Assert.Equal("εντάξει, see you a;yrio", mixed.Typed);
    }
}

internal sealed record MixedLayout(string Code, Script Script, KeyboardLayout Target, KeyboardMap Map, string LayoutId)
{
    public LayoutSource Source => new(Script, Code, Map, LayoutId);

    public LayoutCandidate Candidate => new(Script, Target, Map, Code, LayoutId);
}

// "{en:ну ладно}" is "ну ладно" typed while the English layout was active: the screen shows "ye kflyj" and the
// line should read "ну ладно". Text outside braces was typed on its own layout. Cyrillic picks its layout by
// the letters only one of them has (ы/і), else stays on the line's last Cyrillic layout, else the first listed.
internal sealed class MixedLine
{
    private static readonly MixedLayout[] Known =
    [
        new("en", Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en-US"),
        new("ru", Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru-RU"),
        new("uk", Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk-UA"),
        new("bg", Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.Bg, "bg-BG"),
        new("el", Script.Greek, KeyboardLayout.El, BundledKeyboardMaps.El, "el-GR"),
        new("he", Script.Hebrew, KeyboardLayout.He, BundledKeyboardMaps.He, "he-IL"),
        new("ar", Script.Arabic, KeyboardLayout.Ar, BundledKeyboardMaps.Ar, "ar-SA")
    ];

    private MixedLine(IReadOnlyList<MixedLayout> layouts, string typed, string expected, MixedLayout active)
    {
        Layouts = layouts;
        Typed = typed;
        Expected = expected;
        Active = active;
    }

    public IReadOnlyList<MixedLayout> Layouts { get; }

    public string Typed { get; }

    public string Expected { get; }

    // The layout of the last word typed: what is active when the hotkey goes down.
    public MixedLayout Active { get; }

    public static MixedLayout Find(string code)
    {
        return Known.FirstOrDefault(l => l.Code == code)
               ?? throw new InvalidDataException($"Unknown layout \"{code}\".");
    }

    public static MixedLine Parse(string layouts, string line)
    {
        var installed = layouts.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Find).ToList();
        var typed = new StringBuilder();
        var expected = new StringBuilder();
        MixedLayout? lastCyrillic = null;
        MixedLayout? active = null;

        for (var i = 0; i < line.Length;)
        {
            if (line[i] == '{')
            {
                var colon = line.IndexOf(':', i);
                var close = line.IndexOf('}', i);
                if (colon < 0 || close < colon)
                    throw new InvalidDataException($"Unclosed markup in \"{line}\".");

                var on = Find(line[(i + 1)..colon]);
                if (!installed.Contains(on))
                    throw new InvalidDataException($"\"{on.Code}\" is not among \"{layouts}\" in \"{line}\".");

                var meant = line[(colon + 1)..close];
                var own = OwnLayout(meant, installed, on.Script == Script.Cyrillic ? on : lastCyrillic);
                typed.Append(LayoutTranscoder.Transcode(meant, own.Map, on.Map));
                expected.Append(meant);
                active = on;
                if (on.Script == Script.Cyrillic)
                    lastCyrillic = on;

                i = close + 1;
                continue;
            }

            var end = line.IndexOf('{', i);
            if (end < 0)
                end = line.Length;

            foreach (var word in line[i..end].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Scripts.Dominant(word) is null)
                    continue;

                active = OwnLayout(word, installed, lastCyrillic);
                if (active.Script == Script.Cyrillic)
                    lastCyrillic = active;
            }

            typed.Append(line, i, end - i);
            expected.Append(line, i, end - i);
            i = end;
        }

        return new MixedLine(installed, typed.ToString(), expected.ToString(),
            active ?? throw new InvalidDataException($"No word in \"{line}\"."));
    }

    public string Correct(IPhraseCorrector corrector)
    {
        // As the hotkey: the whole line, every installed layout a candidate, no preferred layout.
        return corrector.CorrectPhrase(
            Typed, Active.Source, Layouts.Select(l => l.Source).ToList(), Layouts.Select(l => l.Candidate).ToList())
            .Corrected;
    }

    private static MixedLayout OwnLayout(string text, IReadOnlyList<MixedLayout> installed, MixedLayout? lastCyrillic)
    {
        var script = Scripts.Dominant(text)
                     ?? throw new InvalidDataException($"\"{text}\" has no letters to tell its layout by.");
        var able = installed.Where(l => l.Script == script && CanType(l.Map, text)).ToList();
        if (able.Count == 0)
            throw new InvalidDataException($"No installed layout types \"{text}\".");

        return lastCyrillic is not null && able.Contains(lastCyrillic) ? lastCyrillic : able[0];
    }

    private static bool CanType(KeyboardMap map, string text)
    {
        foreach (var ch in text)
            if (char.IsLetter(ch) && !map.TryGetKey(ch, out _, out _) && !map.TryGetKey(Decomposed(ch), out _, out _))
                return false;

        return true;
    }

    // Greek accented letters come from a dead key plus the bare letter.
    private static char Decomposed(char ch)
    {
        var form = ch.ToString().Normalize(NormalizationForm.FormD);
        return form.Length > 0 ? form[0] : ch;
    }
}
