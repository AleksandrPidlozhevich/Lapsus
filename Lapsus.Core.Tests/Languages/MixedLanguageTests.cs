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
