using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

public sealed class BrainTests : IDisposable
{
    private const string EnKeys = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
    private const string UkrainianKeys = "ʼйцукенгшщзхїфівапролджєячсмитьбю.";
    private const string RussianKeys = "ёйцукенгшщзхъфывапролджэячсмитьбю.";
    private const string BelarusianKeys = "ёйцукенгшўзх'фывапролджэячсмітьбю.";

    private static readonly KeyboardMap EnMap = new(EnKeys.ToCharArray());
    private static readonly KeyboardMap UkMap = new(UkrainianKeys.ToCharArray());
    private static readonly KeyboardMap RuMap = new(RussianKeys.ToCharArray());
    private static readonly KeyboardMap BeMap = new(BelarusianKeys.ToCharArray());

    private readonly string _enPath;
    private readonly string _ukPath;
    private readonly string _ruPath;
    private readonly LayoutCorrector _corrector;

    public BrainTests()
    {
        _enPath = WriteTempDictionary("en", "cat 100", "car 80", "can 60", "milk 70", "with 90");
        _ukPath = WriteTempDictionary("uk", "кава 100", "молоко 90", "молоком 90", "молоток 90", "з 200");
        _ruPath = WriteTempDictionary("ru", "привет 100", "как 100", "как-цуке 100");

        var spell = new SpellChecker(new[]
        {
            new DictionarySource("en", _enPath, Script.Latin),
            new DictionarySource("uk", _ukPath, Script.Cyrillic),
            new DictionarySource("ru", _ruPath, Script.Cyrillic)
        });
        _corrector = new LayoutCorrector(spell);
    }

    private static readonly List<LayoutCandidate> ToEnglish = [new(Script.Latin, KeyboardLayout.En, EnMap)];
    private static readonly List<LayoutCandidate> ToRussian = [new(Script.Cyrillic, KeyboardLayout.Ru, RuMap)];

    [Fact]
    public void Keys_only_switches_the_layout_but_invents_no_edit()
    {
        var en = new LayoutSource(Script.Latin, "en", EnMap, "en");
        var keysOnly = new CorrectionHints(KeysOnly: true);

        var switched = _corrector.CorrectPhrase("ghbdtn", en, [en], ToRussian, null, keysOnly);
        Assert.Equal("привет", switched.Corrected);

        Assert.Equal("milk", _corrector.CorrectPhrase("mlik", en, [en], ToRussian).Corrected);
        Assert.False(_corrector.CorrectPhrase("mlik", en, [en], ToRussian, null, keysOnly).Changed);
    }

    // "rfrwert" keys to "какцуке"; "как" alone is a dictionary word but "цуке" is not, and only
    // "как-цуке" (one hyphen insertion away) is listed. Keys-only must not let that unreachable edit
    // veto the whole word down to a split that keeps "цуке" as the untransliterated Latin "wert".
    [Fact]
    public void Keys_only_does_not_strand_half_a_word_untransliterated()
    {
        var en = new LayoutSource(Script.Latin, "en", EnMap, "en");
        var keysOnly = new CorrectionHints(KeysOnly: true);

        var switched = _corrector.CorrectPhrase("rfrwert", en, [en], ToRussian, null, keysOnly);
        Assert.Equal("какцуке", switched.Corrected);

        var withTypos = _corrector.CorrectPhrase("rfrwert", en, [en], ToRussian);
        Assert.Equal("как-цуке", withTypos.Corrected);
    }

    [Fact]
    public void Pure_layout_remap_cyrillic_to_latin()
    {
        var result = _corrector.CorrectPhrase("сфк", Script.Cyrillic, UkMap, ToEnglish);

        Assert.True(result.Changed);
        Assert.Equal("car", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void Pure_layout_remap_latin_to_russian()
    {
        var result = _corrector.CorrectPhrase("ghbdtn", Script.Latin, EnMap, ToRussian);

        Assert.True(result.Changed);
        Assert.Equal("привет", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    [Fact]
    public void Fixes_a_word_typed_in_the_wrong_layout()
    {
        var result = _corrector.CorrectPhrase("сфе", Script.Cyrillic, UkMap, ToEnglish);

        Assert.True(result.Changed);
        Assert.Equal("cat", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void In_place_typo_fix_does_not_switch_layout()
    {
        var result = _corrector.CorrectPhrase("молок", Script.Cyrillic, UkMap, ToEnglish);
        Assert.True(result.Changed);
        Assert.Equal("молоко", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Fact]
    public void Leaves_a_valid_word_untouched()
    {
        var result = _corrector.CorrectPhrase("молоко", Script.Cyrillic, UkMap, ToEnglish);
        Assert.False(result.Changed);
        Assert.Equal("молоко", result.Corrected);
    }

    [Fact]
    public void Leaves_a_correct_phrase_untouched()
    {
        var result = _corrector.CorrectPhrase("кава з молоком", Script.Cyrillic, UkMap, ToEnglish);
        Assert.False(result.Changed);
        Assert.Equal("кава з молоком", result.Corrected);
    }

    [Theory]
    [InlineData(KeyboardLayout.Uk, KeyboardLayout.Uk)]
    [InlineData(KeyboardLayout.Be, KeyboardLayout.Be)]
    [InlineData(null, KeyboardLayout.Be)]
    public void Preferred_layout_breaks_a_tie(KeyboardLayout? preferred, KeyboardLayout expected)
    {
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Be, BeMap),
            new(Script.Cyrillic, KeyboardLayout.Uk, UkMap)
        };

        var result = _corrector.CorrectPhrase("rfdf", Script.Latin, EnMap, candidates, preferred);

        Assert.True(result.Changed);
        Assert.Equal("кава", result.Corrected);
        Assert.Equal(expected, result.TargetLayout);
    }

    [Theory]
    [InlineData(KeyboardLayout.Be, KeyboardLayout.Be)]
    [InlineData(KeyboardLayout.Uk, KeyboardLayout.Uk)]
    public void The_preferred_layout_still_breaks_a_tie_between_two_words_of_different_frequency(
        KeyboardLayout preferred, KeyboardLayout expected)
    {
        var bePath = WriteTempDictionary("be-freq", "кава 100", "дом 100000");
        var ukPath = WriteTempDictionary("uk-freq", "кава 5000", "хата 6000");
        try
        {
            var spell = new SpellChecker(
            [
                new DictionarySource("be", bePath, Script.Cyrillic),
                new DictionarySource("uk", ukPath, Script.Cyrillic)
            ]);
            var corrector = new LayoutCorrector(spell);

            var candidates = new List<LayoutCandidate>
            {
                new(Script.Cyrillic, KeyboardLayout.Be, BeMap, "be"),
                new(Script.Cyrillic, KeyboardLayout.Uk, UkMap, "uk")
            };

            var result = corrector.CorrectPhrase("rfdf", Script.Latin, EnMap, candidates, preferred);

            Assert.True(result.Changed);
            Assert.Equal("кава", result.Corrected);
            Assert.Equal(expected, result.TargetLayout);
        }
        finally
        {
            File.Delete(bePath);
            File.Delete(ukPath);
        }
    }

    [Theory]
    [InlineData(null, KeyboardLayout.Uk)]
    [InlineData(KeyboardLayout.Ru, KeyboardLayout.Ru)]
    public void A_word_no_list_has_goes_to_the_language_it_reads_as_when_both_layouts_type_it_alike(
        KeyboardLayout? preferred, KeyboardLayout expected)
    {
        string[] stems = ["кав", "мор", "дуб", "сир", "тин", "вер", "гор", "мак", "риб", "сок", "бук", "дим",
            "жар", "зуб", "кит", "мед", "ніс", "рак", "сад", "там"];
        string[] ukEndings = ["иця", "ицю", "иця", "ині", "ати", "ого", "ими", "ець", "ина", "ову", "иці", "ять"];
        string[] ruEndings = ["ица", "ицу", "ицы", "ине", "ать", "ого", "ыми", "ец", "ина", "ову", "ице", "ят"];
        string[] shared = ["полудень 900", "полумак 900", "полотно 900", "полоса 900", "луна 900", "лунка 900",
            "лунатик 900", "кунак 900", "гуни 900", "дуниш 900"];
        var ukPath = WriteTempDictionary("uk-ngrams", [.. shared, .. Words(stems, ukEndings)]);
        var ruPath = WriteTempDictionary("ru-ngrams", [.. shared, .. Words(stems, ruEndings)]);
        string[] enStems = ["play", "walk", "talk", "jump", "read", "work", "call", "help", "look", "turn", "move",
            "live", "love", "open", "rain", "stay", "wait", "want", "wish", "kick"];
        string[] enEndings = ["", "s", "ed", "ing", "er", "ers", "able", "ful", "less", "ly", "ness"];
        var enPath = WriteTempDictionary("en-ngrams", [.. Words(enStems, enEndings)]);
        try
        {
            var corrector = new LayoutCorrector(new SpellChecker(
            [
                new DictionarySource("en", enPath, Script.Latin),
                new DictionarySource("ru", ruPath, Script.Cyrillic),
                new DictionarySource("uk", ukPath, Script.Cyrillic)
            ]));
            var candidates = new List<LayoutCandidate>
            {
                new(Script.Cyrillic, KeyboardLayout.Ru, RuMap, "ru"),
                new(Script.Cyrillic, KeyboardLayout.Uk, UkMap, "uk")
            };

            var result = corrector.CorrectPhrase("gjkeybwz", Script.Latin, EnMap, candidates, preferred);

            Assert.Equal("полуниця", result.Corrected);
            Assert.Equal(expected, result.TargetLayout);
        }
        finally
        {
            File.Delete(enPath);
            File.Delete(ukPath);
            File.Delete(ruPath);
        }

        static IEnumerable<string> Words(string[] stems, string[] endings)
        {
            var count = 1000;
            foreach (var stem in stems)
                foreach (var ending in endings)
                    yield return $"{stem}{ending} {count--}";
        }
    }

    private static string WriteTempDictionary(string tag, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{tag}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    public void Dispose()
    {
        File.Delete(_enPath);
        File.Delete(_ukPath);
        File.Delete(_ruPath);
    }
}
