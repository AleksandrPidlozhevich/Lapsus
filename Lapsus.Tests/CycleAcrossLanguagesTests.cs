using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using Lapsus.Input;

namespace Lapsus.Tests;

// The line is typed in the active language and ends in one word typed on its keys but meant in another
// script ("…бери ымп" for "svg"). No step of the circle may re-encode the rest, including words missing
// from the list ("скриншоты") and words too short to settle it ("из").
public sealed class CycleAcrossLanguagesTests(CycleAcrossLanguagesTests.Lists lists)
    : IClassFixture<CycleAcrossLanguagesTests.Lists>
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static readonly Language[] Languages =
    [
        new("en", Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En),
        new("ru", Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru),
        new("uk", Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk),
        new("bg", Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.Bg),
        new("el", Script.Greek, KeyboardLayout.El, BundledKeyboardMaps.El),
        new("he", Script.Hebrew, KeyboardLayout.He, BundledKeyboardMaps.He),
        new("ar", Script.Arabic, KeyboardLayout.Ar, BundledKeyboardMaps.Ar)
    ];

    public static TheoryData<string, string, string> Combinations()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var typed in Languages)
        foreach (var meant in Languages)
        {
            if (meant.Script == typed.Script)
                continue;

            data.Add(typed.Code, meant.Code, "");
            foreach (var extra in Languages)
                if (extra != typed && extra != meant)
                    data.Add(typed.Code, meant.Code, extra.Code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void No_step_of_the_circle_re_encodes_the_words_that_were_typed_right(
        string typedIn, string meantIn, string alsoInstalled)
    {
        var typed = Find(typedIn);
        var meant = Find(meantIn);
        var languages = alsoInstalled.Length == 0 ? new[] { typed, meant } : [typed, meant, Find(alsoInstalled)];
        var others = languages.Where(l => l.Script != typed.Script).ToArray();

        var words = lists.Of(typed);
        var line = new List<string>();
        line.AddRange(words.Listed.Take(3));
        line.Add(words.Unlisted[0]);
        if (words.Short.FirstOrDefault(w => ReadsAsNoWordIn(w, typed, others)) is { } shortWord)
            line.Add(shortWord);
        line.Add(words.Unlisted[1]);

        var (fixedWord, onWrongKeys) = WordTypedOnWrongKeys(meant, typed, languages);
        var segment = string.Join(' ', line) + " " + onWrongKeys;
        var kept = string.Join(' ', line) + " ";

        var installed = languages.Select((l, i) => l.Installed(i + 1)).ToList();
        var brain = new LayoutCorrector(new SpellChecker(languages.Select(lists.Source).ToList()));

        var cycle = LayoutCorrectionCycle.Start(brain, segment, installed[0], installed, null);
        Assert.NotNull(cycle);

        var seen = new List<string> { cycle.Current.Text };
        while (seen.Count < 10 && cycle.MoveNext().Text != segment)
            seen.Add(cycle.Current.Text);

        var walk = string.Join(" | ", seen);
        Assert.True(kept + fixedWord == seen[0], $"typed: {segment}\nfirst press: {seen[0]}\nexpected: {kept + fixedWord}");
        Assert.True(cycle.Current.Text == segment, $"the circle never returns to the typed text: {walk}");
        Assert.All(seen, text => Assert.True(text.StartsWith(kept, StringComparison.Ordinal),
            $"a step re-encoded the typed words: {text}\nwalk: {walk}"));
    }

    // "и" is "b" on English keys; the other combinations have nothing that short to trip on.
    public static TheoryData<string, string, string> CombinationsWithAShortTrap()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var row in Combinations())
        {
            var codes = new[] { (string)row[0], (string)row[1], (string)row[2] }.Where(c => c.Length > 0).ToArray();
            var typed = Find(codes[0]);
            var others = codes.Skip(1).Select(Find).Where(l => l.Script != typed.Script).ToArray();
            if (ShortTrapIn(Lists.Full[typed.Code], typed, others) is not null)
                data.Add(codes[0], (string)row[1], (string)row[2]);
        }

        return data;
    }

    // "и что лучше в каком случае …": "и" and "в" are words on English keys too, but the line around them is
    // Russian. Neither the first press nor any later step may turn them into "b", "d".
    [Theory]
    [MemberData(nameof(CombinationsWithAShortTrap))]
    public void A_short_word_that_spells_a_word_on_another_layout_stays(
        string typedIn, string meantIn, string alsoInstalled)
    {
        var (typed, meant, languages) = Setup(typedIn, meantIn, alsoInstalled);
        var words = lists.Of(typed);
        var trap = ShortTrapIn(Lists.Full[typed.Code], typed, languages.Where(l => l.Script != typed.Script))!;

        var line = new[] { words.Listed[0], trap, words.Listed[1], words.Listed[2] };
        var (fixedWord, onWrongKeys) = WordTypedOnWrongKeys(meant, typed, languages);
        var kept = string.Join(' ', line) + " ";

        var seen = Walk(kept + onWrongKeys, typed, languages);

        Assert.True(kept + fixedWord == seen[0], $"typed: {kept + onWrongKeys}\nfirst press: {seen[0]}");
        AssertNoStepReEncodes(seen, kept);
    }

    // The line is right and ends in a word of another script that no list knows ("… выбирать CN№"): the brain
    // leaves it all alone, and the steps that re-type the line on another layout must still spare the typed words.
    [Theory]
    [MemberData(nameof(CombinationsWithAShortTrap))]
    public void With_nothing_to_fix_no_step_re_encodes_the_typed_words(
        string typedIn, string meantIn, string alsoInstalled)
    {
        var (typed, meant, languages) = Setup(typedIn, meantIn, alsoInstalled);
        var words = lists.Of(typed);
        var trap = ShortTrapIn(Lists.Full[typed.Code], typed, languages.Where(l => l.Script != typed.Script))!;

        var kept = string.Join(' ', words.Listed[0], trap, words.Listed[1], words.Listed[2]) + " ";

        var seen = Walk(kept + lists.Of(meant).Unlisted[0], typed, languages);

        AssertNoStepReEncodes(seen, kept);
    }

    private (Language Typed, Language Meant, Language[] Installed) Setup(
        string typedIn, string meantIn, string alsoInstalled)
    {
        var typed = Find(typedIn);
        var meant = Find(meantIn);
        return (typed, meant, alsoInstalled.Length == 0 ? [typed, meant] : [typed, meant, Find(alsoInstalled)]);
    }

    private List<string> Walk(string segment, Language typed, IReadOnlyList<Language> languages)
    {
        var installed = languages.Select((l, i) => l.Installed(i + 1)).ToList();
        var brain = new LayoutCorrector(new SpellChecker(languages.Select(lists.Source).ToList()));

        var cycle = LayoutCorrectionCycle.Start(brain, segment, installed[0], installed, null);
        if (cycle is null)
            return [];

        var seen = new List<string> { cycle.Current.Text };
        while (seen.Count < 10 && cycle.MoveNext().Text != segment)
            seen.Add(cycle.Current.Text);

        Assert.True(cycle.Current.Text == segment, $"the circle never returns to the typed text: {string.Join(" | ", seen)}");
        return seen;
    }

    private static void AssertNoStepReEncodes(List<string> seen, string kept)
    {
        var walk = string.Join(" | ", seen);
        Assert.All(seen, text => Assert.True(text.StartsWith(kept, StringComparison.Ordinal),
            $"a step re-encoded the typed words: {text}\nwalk: {walk}"));
    }

    private static string? ShortTrapIn(
        IEnumerable<(string Word, long Count)> typedWords, Language typed, IEnumerable<Language> others)
    {
        return typedWords.Select(e => e.Word)
            .FirstOrDefault(w => w.Length <= 2 && IsAllLetters(w, typed.Script) &&
                                 others.Any(o => Lists.Full[o.Code].Any(e =>
                                     e.Word == LayoutTranscoder.Transcode(w, typed.Map, o.Map))));
    }

    private (string Word, string OnWrongKeys) WordTypedOnWrongKeys(
        Language meant, Language typed, IReadOnlyList<Language> installed)
    {
        foreach (var word in lists.Of(meant).Listed)
        {
            if (word.Length < 4)
                continue;

            var onKeys = LayoutTranscoder.Transcode(word, meant.Map, typed.Map);
            // Plain keys only: a word that needs Shift on the other layout ("الآن" is "hgNk") is another case.
            if (onKeys == word || !IsAllLetters(onKeys, typed.Script) || onKeys.Any(char.IsUpper) ||
                LayoutTranscoder.Transcode(onKeys, typed.Map, meant.Map) != word ||
                installed.Any(l => lists.Of(l).Knows(onKeys)))
                continue;

            return (word, onKeys);
        }

        throw new InvalidOperationException($"no {meant.Code} word types cleanly on {typed.Code} keys");
    }

    private bool ReadsAsNoWordIn(string word, Language typed, IEnumerable<Language> others)
    {
        return others.All(o => !lists.Of(o).Knows(LayoutTranscoder.Transcode(word, typed.Map, o.Map)));
    }

    private static bool IsAllLetters(string word, Script script)
    {
        return word.Length > 0 && word.All(ch => Scripts.Of(ch) == script);
    }

    private static Language Find(string code)
    {
        return Languages.Single(l => l.Code == code);
    }

    public sealed record Language(string Code, Script Script, KeyboardLayout Layout, KeyboardMap Map)
    {
        public InstalledLayout Installed(int hkl)
        {
            return new InstalledLayout($"{Code}-id", hkl, Script, Code, Layout, Map);
        }
    }

    public sealed class Words(List<string> listed, List<string> unlisted, List<string> shortWords, HashSet<string> all)
    {
        // Commonest first, three letters or more: these settle the line on their own.
        public List<string> Listed { get; } = listed;

        // Ordinary words taken off the list, as a real list lacks "скриншоты".
        public List<string> Unlisted { get; } = unlisted;

        // One or two letters: listed, but too short to settle the line.
        public List<string> Short { get; } = shortWords;

        public bool Knows(string word)
        {
            return all.Contains(word);
        }
    }

    public sealed class Lists : IDisposable
    {
        private readonly Dictionary<string, (Words Words, DictionarySource Source)> _byCode = [];
        private readonly List<string> _paths = [];

        public static readonly Dictionary<string, List<(string Word, long Count)>> Full =
            Languages.ToDictionary(l => l.Code, Read);

        public Lists()
        {
            var full = Full;
            foreach (var language in Languages)
            {
                var entries = full[language.Code];
                var others = Languages.Where(l => l.Script != language.Script).ToArray();

                // Ordinary words with no listed word two edits away in any list of the script (they are pooled: a
                // Bulgarian "затвора" is a neighbour of the Ukrainian "завтра"), so the typo fix has nothing to
                // respell them into, and that no other script spells as a word.
                var words = entries.Select(e => e.Word).ToList();
                var sameScript = Languages.Where(l => l.Script == language.Script)
                    .SelectMany(l => full[l.Code].Select(e => e.Word)).Distinct().ToList();
                var unlisted = words.Skip(100)
                    .Where(w => w.Length >= 6 && IsAllLetters(w, language.Script) &&
                                !sameScript.Any(n => n != w && Math.Abs(n.Length - w.Length) <= 2 && Distance(n, w) <= 2) &&
                                others.All(o => !full[o.Code].Any(e => e.Word ==
                                    LayoutTranscoder.Transcode(w, language.Map, o.Map))))
                    .Take(2)
                    .ToList();

                var kept = entries.Where(e => !unlisted.Contains(e.Word)).ToList();
                var path = Path.Combine(Path.GetTempPath(), $"lapsus-cycle-{language.Code}-{Guid.NewGuid():N}.txt");
                File.WriteAllLines(path, kept.Select(e => $"{e.Word} {e.Count}"));
                _paths.Add(path);

                var listed = kept.Select(e => e.Word).Where(w => w.Length >= 3 && IsAllLetters(w, language.Script)).ToList();
                var shortWords = kept.Select(e => e.Word).Where(w => w.Length <= 2 && IsAllLetters(w, language.Script)).ToList();

                _byCode[language.Code] = (
                    new Words(listed, unlisted, shortWords, kept.Select(e => e.Word).ToHashSet(StringComparer.Ordinal)),
                    new DictionarySource(language.Code, path, language.Script));
            }
        }

        public Words Of(Language language)
        {
            return _byCode[language.Code].Words;
        }

        public DictionarySource Source(Language language)
        {
            return _byCode[language.Code].Source;
        }

        public void Dispose()
        {
            foreach (var path in _paths)
                File.Delete(path);
        }

        // Edits with a swap of two neighbouring letters counted as one, as the typo fix counts them ("завтра" is
        // two edits from "затвора").
        private static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (var i = 0; i <= a.Length; i++)
                d[i, 0] = i;
            for (var j = 0; j <= b.Length; j++)
                d[0, j] = j;

            for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }

            return d[a.Length, b.Length];
        }

        private static List<(string Word, long Count)> Read(Language language)
        {
            var lines = File.ReadLines(Path.Combine(FixtureDir, $"{language.Code}.words.txt"));

            var entries = new List<(string, long)>();
            foreach (var line in lines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && long.TryParse(parts[1], out var count))
                    entries.Add((parts[0], count));
            }

            return entries.Distinct().OrderByDescending(e => e.Item2).ToList();
        }
    }
}
