using System.Collections.Generic;
using System.IO;
using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

public sealed class LayoutSwitchPriorityTests : IDisposable
{
    private readonly string _enPath;
    private readonly string _ruPath;
    private readonly LayoutCorrector _corrector;

    public LayoutSwitchPriorityTests()
    {
        _enPath = WriteTempDictionary("en", "frjane 100", "fryerjane 100", "fryer 100", "narc 100", "jane 100",
            "vs 500", "d 500", "hello 900", "world 800", "that 1000", "i 1000", "think 700", "is 1000",
            "fine 600");
        _ruPath = WriteTempDictionary("ru", "текст 100", "какойто 100", "какой-то 100",
            "мы 900", "в 900", "комнате 300", "привет 900", "еще 900");
        var spell = new SpellChecker(new[]
        {
            new DictionarySource("en", _enPath, Script.Latin),
            new DictionarySource("ru", _ruPath, Script.Cyrillic)
        });
        _corrector = new LayoutCorrector(spell);
    }

    [Fact]
    public void Latin_gibberish_prefers_russian_transcode_over_english_spell_fix()
    {
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru")
        };

        var result = _corrector.CorrectPhrase(
            "fryrfrjqnj ntrcn", Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.True(result.Changed);
        Assert.Contains("текст", result.Corrected);
        Assert.DoesNotContain("fryer", result.Corrected, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    [Fact]
    public void User_reported_russian_phrase_does_not_become_english_spell_fix()
    {
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru")
        };

        var result = _corrector.CorrectPhrase(
            "rfrjqnj ntrcn", Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.True(result.Changed);
        Assert.Equal("какойто текст", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    [Fact]
    public void Bundled_cross_script_fallback_handles_missing_os_candidate()
    {
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en")
        };

        var result = _corrector.CorrectPhrase(
            "rfrjqnj ntrcn", Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.True(result.Changed);
        Assert.Equal("какойто текст", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    [Fact]
    public void English_spell_fix_still_works_when_layout_switch_is_not_plausible()
    {
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en")
        };

        var result = _corrector.CorrectPhrase(
            "fryr", Script.Latin, BundledKeyboardMaps.En, candidates, sourceLanguageCode: "en");

        Assert.True(result.Changed);
        Assert.Equal("fryer", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Fact]
    public void Short_english_hits_are_not_settled_for_the_hotkey_circle()
    {
        var result = _corrector.CorrectPhrase(
            "vs d rjvyfnt", Script.Latin, BundledKeyboardMaps.En, EnAndRu(), sourceLanguageCode: "en");

        Assert.Equal("vs d комнате", result.Corrected);
        Assert.Null(result.Settled);
    }

    [Fact]
    public void Full_english_words_still_vote_down_a_single_russian_word()
    {
        var result = _corrector.CorrectPhrase(
            "hello world ghbdtn", Script.Latin, BundledKeyboardMaps.En, EnAndRu(), sourceLanguageCode: "en");

        Assert.Equal("hello world привет", result.Corrected);
        Assert.Null(result.TargetLayout);
        Assert.Equal(2, result.Settled?.Count);
    }

    [Theory]
    [InlineData("taht", "that")]
    [InlineData("i think taht is fine", "i think that is fine")]
    public void An_english_typo_is_fixed_in_place_not_spell_fixed_into_russian(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(
            typed, Script.Latin, BundledKeyboardMaps.En, EnAndRu(), sourceLanguageCode: "en");

        Assert.Equal(expected, result.Corrected);
    }

    [Fact]
    public void A_wrong_layout_word_on_the_keys_alone_still_beats_the_typo_fix()
    {
        var result = _corrector.CorrectPhrase(
            "ghbdtn", Script.Latin, BundledKeyboardMaps.En, EnAndRu(), sourceLanguageCode: "en");

        Assert.Equal("привет", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    private static List<LayoutCandidate> EnAndRu()
    {
        return
        [
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en"),
            new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru")
        ];
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
        File.Delete(_ruPath);
    }
}
