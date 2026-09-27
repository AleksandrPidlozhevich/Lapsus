using System.Collections.Generic;
using System.IO;
using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Languages.Georgian;

public sealed class GeorgianCorrectionTests : IDisposable
{
    private static readonly string[] GeorgianWords =
    [
        "გამარჯობა 900",
        "მადლობა 800",
        "ქართული 700",
        "თბილისი 600",
        "წიგნი 500",
        "კარგი 400",
        "მან 300"
    ];

    private static readonly string[] EnglishWords = ["hello 900", "text 800", "world 700", "man 600"];

    private static readonly string[] UkrainianWords = ["привіт 900", "дякую 800", "будинок 700"];

    private static readonly List<LayoutCandidate> ToGeorgian =
        [new(Script.Georgian, KeyboardLayout.Ka, BundledKeyboardMaps.Ka, "ka")];

    private static readonly List<LayoutCandidate> ToLatin =
        [new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en")];

    private static readonly List<LayoutCandidate> ToUkrainian =
        [new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")];

    private readonly List<string> _files = [];
    private readonly DictionarySource _georgian;
    private readonly DictionarySource _english;
    private readonly DictionarySource _ukrainian;

    private readonly LayoutCorrector _corrector;

    public GeorgianCorrectionTests()
    {
        _georgian = WriteDictionary("ka", Script.Georgian, GeorgianWords);
        _english = WriteDictionary("en", Script.Latin, EnglishWords);
        _ukrainian = WriteDictionary("uk", Script.Cyrillic, UkrainianWords);
        _corrector = CorrectorWith(_georgian, _english);
    }

    public void Dispose()
    {
        foreach (var file in _files)
            File.Delete(file);
    }

    private DictionarySource WriteDictionary(string code, Script script, string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{code}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        _files.Add(path);
        return new DictionarySource(code, path, script);
    }

    private static LayoutCorrector CorrectorWith(params DictionarySource[] dictionaries)
    {
        return new LayoutCorrector(new SpellChecker(dictionaries));
    }

    [Fact]
    public void Latin_keystrokes_switch_to_georgian()
    {
        var result = _corrector.CorrectPhrase(
            "gamarjoba", Script.Latin, BundledKeyboardMaps.En, ToGeorgian);

        Assert.True(result.Changed);
        Assert.Equal("გამარჯობა", result.Corrected);
        Assert.Equal(KeyboardLayout.Ka, result.TargetLayout);
    }

    [Fact]
    public void Shift_level_letters_survive_the_switch()
    {
        var result = _corrector.CorrectPhrase(
            "qarTuli", Script.Latin, BundledKeyboardMaps.En, ToGeorgian);

        Assert.True(result.Changed);
        Assert.Equal("ქართული", result.Corrected);
    }

    [Fact]
    public void Latin_keystrokes_with_a_typo_switch_and_get_fixed()
    {
        var result = _corrector.CorrectPhrase(
            "madlba", Script.Latin, BundledKeyboardMaps.En, ToGeorgian);

        Assert.True(result.Changed);
        Assert.Equal("მადლობა", result.Corrected);
        Assert.Equal(KeyboardLayout.Ka, result.TargetLayout);
    }

    [Fact]
    public void Phrase_is_corrected_word_by_word()
    {
        var result = _corrector.CorrectPhrase(
            "gamarjoba Tbilisi", Script.Latin, BundledKeyboardMaps.En, ToGeorgian);

        Assert.True(result.Changed);
        Assert.Equal("გამარჯობა თბილისი", result.Corrected);
    }

    [Fact]
    public void Offline_phrase_path_offers_the_georgian_candidate()
    {
        var result = _corrector.CorrectPhrase("gamarjoba");

        Assert.True(result.Changed);
        Assert.Equal("გამარჯობა", result.Corrected);
        Assert.Equal(KeyboardLayout.Ka, result.TargetLayout);
    }

    [Fact]
    public void Cyrillic_keystrokes_switch_to_georgian()
    {
        var result = _corrector.CorrectPhrase(
            "пфьфкощиф", Script.Cyrillic, BundledKeyboardMaps.Uk, ToGeorgian);

        Assert.True(result.Changed);
        Assert.Equal("გამარჯობა", result.Corrected);
        Assert.Equal(KeyboardLayout.Ka, result.TargetLayout);
    }

    [Fact]
    public void A_shifted_level_letter_survives_a_layout_that_has_none()
    {
        var result = _corrector.CorrectPhrase(
            "йфкЕгдш", Script.Cyrillic, BundledKeyboardMaps.Uk, ToGeorgian);

        Assert.True(result.Changed);
        Assert.Equal("ქართული", result.Corrected);
    }

    [Fact]
    public void Georgian_keystrokes_switch_back_to_latin()
    {
        var result = _corrector.CorrectPhrase(
            "ჰელლო", Script.Georgian, BundledKeyboardMaps.Ka, ToLatin);

        Assert.True(result.Changed);
        Assert.Equal("hello", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void Georgian_keystrokes_switch_back_to_cyrillic()
    {
        var typed = LayoutTranscoder.Transcode("привіт", BundledKeyboardMaps.Uk, BundledKeyboardMaps.Ka);
        Assert.Equal("გჰბდსნ", typed);

        var result = CorrectorWith(_georgian, _ukrainian)
            .CorrectPhrase(typed, Script.Georgian, BundledKeyboardMaps.Ka, ToUkrainian);

        Assert.True(result.Changed);
        Assert.Equal("привіт", result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
    }

    [Fact]
    public void In_place_georgian_typo_fix_keeps_the_layout()
    {
        var result = _corrector.CorrectPhrase(
            "მადლბა", Script.Georgian, BundledKeyboardMaps.Ka, ToGeorgian);

        Assert.True(result.Changed);
        Assert.Equal("მადლობა", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Fact]
    public void Real_georgian_is_left_alone()
    {
        var result = _corrector.CorrectPhrase(
            "გამარჯობა", Script.Georgian, BundledKeyboardMaps.Ka, ToLatin);

        Assert.False(result.Changed);
        Assert.Equal("გამარჯობა", result.Corrected);
    }

    [Fact]
    public void Real_english_is_not_dragged_into_georgian()
    {
        var result = _corrector.CorrectPhrase(
            "hello world", Script.Latin, BundledKeyboardMaps.En, ToGeorgian);

        Assert.False(result.Changed);
        Assert.Equal("hello world", result.Corrected);
    }

    [Fact]
    public void Real_ukrainian_is_not_dragged_into_georgian()
    {
        var result = CorrectorWith(_georgian, _ukrainian)
            .CorrectPhrase("привіт", Script.Cyrillic, BundledKeyboardMaps.Uk, ToGeorgian);

        Assert.False(result.Changed);
        Assert.Equal("привіт", result.Corrected);
    }

    [Fact]
    public void A_word_real_on_both_sides_is_left_as_typed()
    {
        Assert.False(_corrector.CorrectPhrase(
            "მან", Script.Georgian, BundledKeyboardMaps.Ka, ToLatin).Changed);
        Assert.False(_corrector.CorrectPhrase(
            "man", Script.Latin, BundledKeyboardMaps.En, ToGeorgian).Changed);
    }

    [Fact]
    public void Trailing_punctuation_stays_punctuation()
    {
        var result = _corrector.CorrectPhrase(
            "gamarjoba,", Script.Latin, BundledKeyboardMaps.En, ToGeorgian);

        Assert.Equal("გამარჯობა,", result.Corrected);
    }

    [Fact]
    public void With_no_georgian_dictionary_georgian_is_not_dragged_into_english()
    {
        var result = CorrectorWith(_english)
            .CorrectPhrase("მან", Script.Georgian, BundledKeyboardMaps.Ka, ToLatin);

        Assert.False(result.Changed);
        Assert.Equal("მან", result.Corrected);
    }

    [Fact]
    public void With_no_georgian_dictionary_cyrillic_is_still_reachable()
    {
        var typed = LayoutTranscoder.Transcode("привіт", BundledKeyboardMaps.Uk, BundledKeyboardMaps.Ka);
        var ukrainianOnly = CorrectorWith(_ukrainian);

        var result = ukrainianOnly.CorrectPhrase(typed, Script.Georgian, BundledKeyboardMaps.Ka, ToUkrainian);

        Assert.True(result.Changed);
        Assert.Equal("привіт", result.Corrected);
    }

    [Fact]
    public void Georgian_is_never_rewritten_into_cyrillic_noise()
    {
        var ukrainianOnly = CorrectorWith(_ukrainian);

        var result = ukrainianOnly.CorrectPhrase(
            "უსაფრთხოების", Script.Georgian, BundledKeyboardMaps.Ka, ToUkrainian);

        Assert.False(result.Changed);
        Assert.Equal("უსაფრთხოების", result.Corrected);
    }

    [Fact]
    public void With_no_dictionary_at_all_nothing_moves_either_way()
    {
        var blind = CorrectorWith();

        Assert.False(blind.CorrectPhrase(
            "gamarjoba", Script.Latin, BundledKeyboardMaps.En, ToGeorgian).Changed);
        Assert.False(blind.CorrectPhrase(
            "გამარჯობა", Script.Georgian, BundledKeyboardMaps.Ka, ToLatin).Changed);
    }

    [Fact]
    public void A_real_english_word_survives_beside_a_wrong_layout_georgian_one()
    {
        var result = _corrector.CorrectPhrase(
            "gamarjoba hello", Script.Latin, BundledKeyboardMaps.En, ToGeorgian);

        Assert.True(result.Changed);
        Assert.Equal("გამარჯობა hello", result.Corrected);
    }
}
