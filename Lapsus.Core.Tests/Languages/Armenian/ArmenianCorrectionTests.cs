using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Languages.Armenian;

// Keys are the Windows Armenian Phonetic layout: the digit row carries letters (8 = ր, 7 = և, 2 = թ).
public sealed class ArmenianCorrectionTests
{
    private static readonly string[] ArmenianWords =
    [
        "հայերեն 900",
        "բարեւ 800", // Unilex spells the ligature out; the keyboard types և
        "դաս 700",
        "տուն 600",
        "ինչու 500",
        "մայր 400",
        "ման 300"
    ];

    private static readonly string[] EnglishWords = ["hello 900", "text 800", "world 700", "man 600"];

    private static readonly string[] UkrainianWords = ["вітання 900", "дякую 800", "будинок 700"];

    private static readonly List<LayoutCandidate> ToArmenian =
        [new(Script.Armenian, KeyboardLayout.Hy, BundledKeyboardMaps.Hy, "hy")];

    private static readonly List<LayoutCandidate> ToLatin =
        [new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en")];

    private static readonly List<LayoutCandidate> ToUkrainian =
        [new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")];

    private readonly DictionarySource _armenian;
    private readonly DictionarySource _english;
    private readonly DictionarySource _ukrainian;

    private readonly LayoutCorrector _corrector;

    public ArmenianCorrectionTests()
    {
        _armenian = WriteDictionary("hy", Script.Armenian, ArmenianWords);
        _english = WriteDictionary("en", Script.Latin, EnglishWords);
        _ukrainian = WriteDictionary("uk", Script.Cyrillic, UkrainianWords);
        _corrector = CorrectorWith(_armenian, _english);
    }

    private static DictionarySource WriteDictionary(string code, Script script, string[] lines)
    {
        return new DictionarySource(code, TestDictionaries.List(code, lines), script);
    }

    private static LayoutCorrector CorrectorWith(params DictionarySource[] dictionaries)
    {
        return new LayoutCorrector(new SpellChecker(dictionaries));
    }

    [Theory]
    [InlineData("das", "դաս")]
    [InlineData("twun", "տուն")]
    [InlineData("haje8en", "հայերեն")]
    [InlineData("maj8", "մայր")]
    public void Latin_keystrokes_switch_to_armenian(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(typed, Script.Latin, BundledKeyboardMaps.En, ToArmenian);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.Hy, result.TargetLayout);
    }

    [Fact]
    public void The_ligature_key_finds_the_spelled_out_word()
    {
        var result = _corrector.CorrectPhrase("ba87", Script.Latin, BundledKeyboardMaps.En, ToArmenian);

        Assert.True(result.Changed);
        Assert.Equal("բարև", result.Corrected);
    }

    [Fact]
    public void A_capital_stays_a_capital()
    {
        var result = _corrector.CorrectPhrase("Haje8en", Script.Latin, BundledKeyboardMaps.En, ToArmenian);

        Assert.Equal("Հայերեն", result.Corrected);
    }

    [Fact]
    public void Cyrillic_keystrokes_switch_to_armenian()
    {
        var typed = LayoutTranscoder.Transcode("դաս", BundledKeyboardMaps.Hy, BundledKeyboardMaps.Uk);

        var result = CorrectorWith(_armenian, _ukrainian)
            .CorrectPhrase(typed, Script.Cyrillic, BundledKeyboardMaps.Uk, ToArmenian);

        Assert.True(result.Changed);
        Assert.Equal("դաս", result.Corrected);
    }

    [Fact]
    public void Armenian_keystrokes_switch_back_to_latin()
    {
        var result = _corrector.CorrectPhrase("հելլօ", Script.Armenian, BundledKeyboardMaps.Hy, ToLatin);

        Assert.True(result.Changed);
        Assert.Equal("hello", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void Armenian_keystrokes_switch_back_to_cyrillic()
    {
        var typed = LayoutTranscoder.Transcode("вітання", BundledKeyboardMaps.Uk, BundledKeyboardMaps.Hy);

        var result = CorrectorWith(_armenian, _ukrainian)
            .CorrectPhrase(typed, Script.Armenian, BundledKeyboardMaps.Hy, ToUkrainian);

        Assert.True(result.Changed);
        Assert.Equal("вітання", result.Corrected);
    }

    [Fact]
    public void In_place_armenian_typo_fix_keeps_the_layout()
    {
        var result = _corrector.CorrectPhrase("հայեեն", Script.Armenian, BundledKeyboardMaps.Hy, ToArmenian);

        Assert.True(result.Changed);
        Assert.Equal("հայերեն", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Theory]
    [InlineData("հայերեն")]
    [InlineData("բարև")]
    [InlineData("ինչո՞ւ")] // the question mark sits over the stressed vowel
    public void Real_armenian_is_left_alone(string word)
    {
        var result = _corrector.CorrectPhrase(word, Script.Armenian, BundledKeyboardMaps.Hy, ToLatin);

        Assert.Equal(word, result.Corrected);
        Assert.False(result.Changed);
    }

    [Fact]
    public void Real_english_is_not_dragged_into_armenian()
    {
        var result = _corrector.CorrectPhrase("hello world", Script.Latin, BundledKeyboardMaps.En, ToArmenian);

        Assert.False(result.Changed);
    }

    [Fact]
    public void A_word_real_on_both_sides_is_left_as_typed()
    {
        Assert.False(_corrector.CorrectPhrase("ման", Script.Armenian, BundledKeyboardMaps.Hy, ToLatin).Changed);
        Assert.False(_corrector.CorrectPhrase("man", Script.Latin, BundledKeyboardMaps.En, ToArmenian).Changed);
    }

    [Fact]
    public void With_no_armenian_dictionary_armenian_is_not_dragged_into_english()
    {
        var result = CorrectorWith(_english)
            .CorrectPhrase("ման", Script.Armenian, BundledKeyboardMaps.Hy, ToLatin);

        Assert.False(result.Changed);
    }

    [Fact]
    public void With_no_dictionary_at_all_nothing_moves_either_way()
    {
        var blind = CorrectorWith();

        Assert.False(blind.CorrectPhrase("haje8en", Script.Latin, BundledKeyboardMaps.En, ToArmenian).Changed);
        Assert.False(blind.CorrectPhrase("հայերեն", Script.Armenian, BundledKeyboardMaps.Hy, ToLatin).Changed);
    }

    private static readonly LayoutSource EnglishKeys = new(Script.Latin, "en", BundledKeyboardMaps.En, "en-US");

    private static readonly LayoutSource ArmenianKeys = new(Script.Armenian, "hy", BundledKeyboardMaps.Hy, "hy-id");

    private static readonly LayoutCandidate[] EnglishAndArmenian =
    [
        new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en-US"),
        new(Script.Armenian, KeyboardLayout.Hy, BundledKeyboardMaps.Hy, "hy", "hy-id")
    ];

    // As the backend runs a word after Space, with typo fixing on; the fixture lists carry the noise (ղ) and
    // the neighbours (հետ, կես) that tech tokens used to be dragged into.
    private static string AutoFix(string word)
    {
        var corrector = new LayoutCorrector(new SpellChecker(
        [
            new DictionarySource("en", TestDictionaries.Fixture("en"), Script.Latin),
            new DictionarySource("hy", TestDictionaries.Fixture("hy"), Script.Armenian)
        ]));
        var hints = new CorrectionHints(KeysOnly: AutoCorrectPolicy.KeysOnlyFor(word, keysOnly: false));
        var corrected = corrector.CorrectPhrase(
            word, EnglishKeys, [EnglishKeys, ArmenianKeys], EnglishAndArmenian, null, hints).Corrected;

        return AutoCorrectPolicy.ReadsThrough(word, corrected) ? corrected : word;
    }

    [Theory]
    [InlineData("h2o")]
    [InlineData("k8s")]
    [InlineData("i18n")]
    [InlineData("a11y")]
    [InlineData("mp3")]
    [InlineData("sha256")]
    [InlineData("x86")]
    public void Auto_fix_leaves_a_token_with_digits_alone(string token)
    {
        Assert.Equal(token, AutoFix(token));
    }

    [Theory]
    [InlineData("haje8en", "հայերեն")]
    [InlineData("ajso8", "այսօր")]
    [InlineData("9em", "չեմ")]
    [InlineData("in9pes", "ինչպես")]
    public void Auto_fix_still_reads_digit_keys_as_armenian_letters(string typed, string expected)
    {
        Assert.Equal(expected, AutoFix(typed));
    }

    [Fact]
    public void Digits_after_a_crossed_word_stay_digits()
    {
        var corrector = CorrectorWith(WriteDictionary("hy", Script.Armenian, ["ղ 900"]), _english);

        var result = corrector.CorrectPhrase("x86", Script.Latin, BundledKeyboardMaps.En, ToArmenian);

        Assert.NotEqual("ղրւ", result.Corrected);
    }

    [Fact]
    public void A_real_english_word_survives_beside_a_wrong_layout_armenian_one()
    {
        var result = _corrector.CorrectPhrase("haje8en hello", Script.Latin, BundledKeyboardMaps.En, ToArmenian);

        Assert.Equal("հայերեն hello", result.Corrected);
    }
}
