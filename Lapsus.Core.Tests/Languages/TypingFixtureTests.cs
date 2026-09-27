using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Languages;

// A pending fixture row must still fail; closing the gap flips the row.
public sealed class TypingFixtureTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Languages", "Fixtures");

    private static readonly LayoutCandidate English =
        new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en-US");

    private sealed record Language(string Code, Script Script, KeyboardLayout Layout, KeyboardMap Map)
    {
        public LayoutCandidate Candidate => new(Script, Layout, Map, Code, $"{Code}-id");

        public LayoutSource Source => new(Script, Code, Map, $"{Code}-id");
    }

    private static readonly Dictionary<string, Language> Languages = new()
    {
        ["uk"] = new("uk", Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk),
        ["he"] = new("he", Script.Hebrew, KeyboardLayout.He, BundledKeyboardMaps.He),
        ["ar"] = new("ar", Script.Arabic, KeyboardLayout.Ar, BundledKeyboardMaps.Ar),
        ["el"] = new("el", Script.Greek, KeyboardLayout.El, BundledKeyboardMaps.El),
        ["bg"] = new("bg", Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.Bg),
        ["bg-bds"] = new("bg", Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.BgBds)
    };

    public static TheoryData<string, string, string, string> Cases(string fixture)
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var line in File.ReadLines(Path.Combine(FixtureDir, $"{fixture}.cases.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var fields = line.Split('\t');
            data.Add(fields[0], fields[1], fields[2], fields.Length > 3 ? fields[3] : string.Empty);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases), "uk")]
    public void Ukrainian(string typed, string expected, string status, string note) => Check("uk", typed, expected, status, note);

    [Theory]
    [MemberData(nameof(Cases), "he")]
    public void Hebrew(string typed, string expected, string status, string note) => Check("he", typed, expected, status, note);

    [Theory]
    [MemberData(nameof(Cases), "ar")]
    public void Arabic(string typed, string expected, string status, string note) => Check("ar", typed, expected, status, note);

    [Theory]
    [MemberData(nameof(Cases), "el")]
    public void Greek(string typed, string expected, string status, string note) => Check("el", typed, expected, status, note);

    [Theory]
    [MemberData(nameof(Cases), "bg")]
    public void Bulgarian(string typed, string expected, string status, string note) => Check("bg", typed, expected, status, note);

    [Theory]
    [MemberData(nameof(Cases), "bg-bds")]
    public void Bulgarian_bds(string typed, string expected, string status, string note) => Check("bg-bds", typed, expected, status, note);

    private static void Check(string fixture, string typed, string expected, string status, string note)
    {
        var language = Languages[fixture];
        var corrector = new LayoutCorrector(new SpellChecker(
        [
            new DictionarySource("en", Path.Combine(FixtureDir, "en.words.txt"), Script.Latin),
            new DictionarySource(language.Code, Path.Combine(FixtureDir, $"{language.Code}.words.txt"), language.Script)
        ]));

        var english = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En, "en-US");
        var active = Scripts.Dominant(typed) == language.Script ? language.Source : english;
        var installed = new[] { english, language.Source };
        var candidates = new[] { English, language.Candidate };

        var got = corrector.CorrectPhrase(typed, active, installed, candidates).Corrected;

        switch (status)
        {
            case "ok":
                Assert.True(expected == got, $"{note}: \"{typed}\" → \"{got}\", wanted \"{expected}\"");
                break;
            case "pending":
                Assert.True(expected != got, $"{note}: \"{typed}\" now comes back right — mark the row ok");
                break;
            default:
                throw new InvalidDataException($"Unknown status \"{status}\" for \"{typed}\"; use ok or pending.");
        }
    }
}
