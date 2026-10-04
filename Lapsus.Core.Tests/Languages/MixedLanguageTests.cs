using System.Collections.Generic;
using System.IO;
using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Languages;

public sealed class MixedLanguageTests : IDisposable
{
    private const string EnKeys = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
    private const string UkrainianKeys = "ʼйцукенгшщзхїфівапролджєячсмитьбю.";
    private const string RussianKeys = "ёйцукенгшщзхъфывапролджэячсмитьбю.";
    private const string GreekKeys = "`;ςερτυθιοπ[]ασδφγηξκλ΄'ζχψωβνμ,./";

    private static readonly KeyboardMap EnMap = new(EnKeys.ToCharArray());
    private static readonly KeyboardMap UkMap = new(UkrainianKeys.ToCharArray());
    private static readonly KeyboardMap RuMap = new(RussianKeys.ToCharArray());
    private static readonly KeyboardMap ElMap = new(GreekKeys.ToCharArray());

    private readonly List<string> _paths = [];
    private readonly LayoutCorrector _corrector;

    public MixedLanguageTests()
    {
        _corrector = new LayoutCorrector(new SpellChecker(
        [
            new DictionarySource("en", Temp("en", "cat 900", "server 800", "the 950"), Script.Latin),
            new DictionarySource("pl", Temp("pl", "dobry 850", "dzień 900", "mleko 800"), Script.Latin),
            new DictionarySource("uk", Temp("uk", "вітання 900", "кава 800", "молоко 800"), Script.Cyrillic),
            new DictionarySource("ru", Temp("ru", "привет 900", "кофе 800", "молоко 800"), Script.Cyrillic),
            new DictionarySource("el", Temp("el", "καλημέρα 900", "κόσμος 800"), Script.Greek)
        ]));
    }

    private static readonly LayoutSource ActiveEnglish = new(Script.Latin, "en", EnMap, "en-id");

    private static readonly List<LayoutSource> InstalledUkrainian =
    [
        ActiveEnglish,
        new(Script.Cyrillic, "uk", UkMap, "uk-id"),
        new(Script.Greek, "el", ElMap, "el-id")
    ];

    private static readonly List<LayoutCandidate> ToUkrainian =
    [
        new(Script.Latin, KeyboardLayout.En, EnMap, "en", "en-id"),
        new(Script.Cyrillic, KeyboardLayout.Uk, UkMap, "uk", "uk-id"),
        new(Script.Greek, KeyboardLayout.El, ElMap, "el", "el-id")
    ];

    private static readonly List<LayoutSource> InstalledBothCyrillic =
    [
        ActiveEnglish,
        new(Script.Cyrillic, "ru", RuMap, "ru-id"),
        new(Script.Cyrillic, "uk", UkMap, "uk-id")
    ];

    private static readonly List<LayoutCandidate> ToBothCyrillic =
    [
        new(Script.Latin, KeyboardLayout.En, EnMap, "en", "en-id"),
        new(Script.Cyrillic, KeyboardLayout.Ru, RuMap, "ru", "ru-id"),
        new(Script.Cyrillic, KeyboardLayout.Uk, UkMap, "uk", "uk-id")
    ];

    [Theory]
    [InlineData("вітання cat")]
    [InlineData("кава dobry")]
    [InlineData("вітання καλημέρα")]
    [InlineData("кава καλημέρα cat")]
    public void A_correct_line_of_several_scripts_is_left_alone(string text)
    {
        var result = _corrector.CorrectPhrase(text, ActiveEnglish, InstalledUkrainian, ToUkrainian);

        Assert.False(result.Changed);
        Assert.Equal(text, result.Corrected);
    }

    [Fact]
    public void A_real_word_beside_a_wrong_layout_one_survives_the_fix()
    {
        var result = _corrector.CorrectPhrase("dsnfyyz cat", ActiveEnglish, InstalledUkrainian, ToUkrainian);

        Assert.True(result.Changed);
        Assert.Equal("вітання cat", result.Corrected);
        Assert.Equal("uk-id", result.TargetLayoutId);
    }

    [Fact]
    public void A_real_word_survives_a_run_of_wrong_layout_words()
    {
        var result = _corrector.CorrectPhrase(
            "dsnfyyz rfdf server", ActiveEnglish, InstalledUkrainian, ToUkrainian);

        Assert.True(result.Changed);
        Assert.Equal("вітання кава server", result.Corrected);
        Assert.Equal("uk-id", result.TargetLayoutId);
    }

    [Fact]
    public void Greek_typed_on_the_english_layout_is_recovered()
    {
        var result = _corrector.CorrectPhrase("kalhmera", ActiveEnglish, InstalledUkrainian, ToUkrainian);

        Assert.True(result.Changed);
        Assert.Equal("καλημέρα", result.Corrected);
        Assert.Equal("el-id", result.TargetLayoutId);
    }

    [Fact]
    public void Two_languages_of_one_script_both_stand()
    {
        var result = _corrector.CorrectPhrase("dobry server", ActiveEnglish, InstalledUkrainian, ToUkrainian);

        Assert.False(result.Changed);
        Assert.Equal("dobry server", result.Corrected);
    }

    [Theory]
    [InlineData("dsnfyyz", "вітання", "uk-id")]
    [InlineData("ghbdtn", "привет", "ru-id")]
    public void Two_cyrillic_languages_are_told_apart_by_whichever_owns_the_word(
        string typed, string expected, string expectedLayoutId)
    {
        var result = _corrector.CorrectPhrase(typed, ActiveEnglish, InstalledBothCyrillic, ToBothCyrillic);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(expectedLayoutId, result.TargetLayoutId);
    }

    [Theory]
    [InlineData(KeyboardLayout.Uk, "uk-id")]
    [InlineData(KeyboardLayout.Ru, "ru-id")]
    public void A_word_both_cyrillic_languages_know_follows_the_preferred_layout(
        KeyboardLayout preferred, string expectedLayoutId)
    {
        var result = _corrector.CorrectPhrase(
            "vjkjrj", ActiveEnglish, InstalledBothCyrillic, ToBothCyrillic, preferred);

        Assert.True(result.Changed);
        Assert.Equal("молоко", result.Corrected);
        Assert.Equal(expectedLayoutId, result.TargetLayoutId);
    }

    [Fact]
    public void A_line_of_two_cyrillic_languages_corrects_every_word()
    {
        var result = _corrector.CorrectPhrase(
            "dsnfyyz ghbdtn", ActiveEnglish, InstalledBothCyrillic, ToBothCyrillic);

        Assert.True(result.Changed);
        Assert.Equal("вітання привет", result.Corrected);
    }

    // Real-keyboard lines where the language changes mid-way; counts follow the fixture lists.
    private static readonly string[] English =
    [
        "you 28787591", "the 22761659", "to 17099834", "me 6444985", "this 5739788", "know 3892394",
        "see 1781493", "let 1705262", "please 842120", "check 176507", "send 131999", "song 86877",
        "report 77824", "code 35711", "review 30000", "s 110199",
        "gia 3000", "ola 2500", "sto 2000"
    ];

    private static readonly string[] Greek =
    [
        "να 9569246", "το 7460197", "μου 3566240", "με 3186744", "για 3083034", "αυτό 2360488",
        "στο 1831681", "είσαι 1001826", "όλα 415449", "ευχαριστώ 408824", "αρέσει 159218", "αύριο 94465",
        "πότε 85776", "πρωί 72211", "σον 2000"
    ];

    private static readonly string[] Ukrainian =
    [
        "і 900000", "мені 300000", "завтра 60000", "напиши 5000"
    ];

    private static readonly LayoutSource RealEnglish = new(Script.Latin, "en", BundledKeyboardMaps.En, "en-US");

    private static PhraseCorrection CorrectMixed(
        string text, string code, KeyboardLayout layout, string[] words, bool activeIsTheOther = false)
    {
        using var harness = new ScriptHarness()
            .With("en", Script.Latin, English)
            .With(code, ScriptLayouts.ScriptOf(layout), words);

        var other = new LayoutSource(ScriptLayouts.ScriptOf(layout), code, Layouts.Map(layout), code + "-id");
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en-US"),
            Layouts.To(layout, code)
        };

        return harness.Corrector().CorrectPhrase(
            text, activeIsTheOther ? other : RealEnglish, [RealEnglish, other], candidates);
    }

    [Fact]
    public void A_word_both_languages_know_goes_with_the_crossed_words_around_it()
    {
        var result = CorrectMixed("send the report a;yrio to prv;i", "el", KeyboardLayout.El, Greek);

        Assert.Equal("send the report αύριο το πρωί", result.Corrected);
    }

    [Fact]
    public void A_short_word_at_the_seam_of_a_mostly_english_line_stays_english()
    {
        var result = CorrectMixed("send the report to a;yrio prv;i", "el", KeyboardLayout.El, Greek);

        Assert.Equal("send the report to αύριο πρωί", result.Corrected);
    }

    [Fact]
    public void A_short_word_between_two_real_english_words_keeps_its_english()
    {
        var result = CorrectMixed("let me know p;ote e;isai", "el", KeyboardLayout.El, Greek);

        Assert.Equal("let me know πότε είσαι", result.Corrected);
    }

    [Fact]
    public void Words_after_one_crossing_follow_it_when_the_other_reading_is_far_commoner()
    {
        var result = CorrectMixed("see you eyxarist;v gia ;ola", "el", KeyboardLayout.El, Greek);

        Assert.Equal("see you ευχαριστώ για όλα", result.Corrected);
    }

    [Fact]
    public void A_greek_word_typed_in_greek_stays_between_english_ones()
    {
        var result = CorrectMixed("ρεωιες στο ψοδε", "el", KeyboardLayout.El, Greek, activeIsTheOther: true);

        Assert.Equal("review στο code", result.Corrected);
    }

    [Fact]
    public void An_english_word_typed_on_the_greek_layout_is_not_taken_for_a_greek_typo()
    {
        var result = CorrectMixed("μου αρέσει αυτό το σονγ", "el", KeyboardLayout.El, Greek, activeIsTheOther: true);

        Assert.Equal("μου αρέσει αυτό το song", result.Corrected);
    }

    [Fact]
    public void A_one_letter_word_joins_the_ukrainian_half_of_the_line()
    {
        var result = CorrectMixed("check this s yfgbib vtys pfdnhf", "uk", KeyboardLayout.Uk, Ukrainian);

        Assert.Equal("check this і напиши мені завтра", result.Corrected);
    }

    private string Temp(string tag, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{tag}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        _paths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _paths)
            File.Delete(path);
    }
}
