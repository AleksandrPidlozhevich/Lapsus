using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Languages.Hebrew;

public sealed class HebrewCorrectionTests : IDisposable
{
    private readonly string _hePath;
    private readonly string _enPath;
    private readonly LayoutCorrector _corrector;

    private static readonly List<LayoutCandidate> ToHebrew =
        [new(Script.Hebrew, KeyboardLayout.He, BundledKeyboardMaps.He, "he")];

    private static readonly List<LayoutCandidate> ToLatin =
        [new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en")];

    public HebrewCorrectionTests()
    {
        _hePath = Path.Combine(Path.GetTempPath(), $"lapsus-he-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(_hePath,
        [
            "שלום 900",
            "תודה 800",
            "בוקר 700",
            "ספר 600"
        ]);

        _enPath = Path.Combine(Path.GetTempPath(), $"lapsus-en-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(_enPath,
        [
            "ship 900",
            "text 800",
            "hello 700"
        ]);

        var spell = new SpellChecker(
        [
            new DictionarySource("he", _hePath, Script.Hebrew),
            new DictionarySource("en", _enPath, Script.Latin)
        ]);
        _corrector = new LayoutCorrector(spell);
    }

    public void Dispose()
    {
        File.Delete(_hePath);
        File.Delete(_enPath);
    }

    [Fact]
    public void Latin_keystrokes_switch_to_hebrew()
    {
        var result = _corrector.CorrectPhrase(
            "akuo", Script.Latin, BundledKeyboardMaps.En, ToHebrew);

        Assert.True(result.Changed);
        Assert.Equal("שלום", result.Corrected);
        Assert.Equal(KeyboardLayout.He, result.TargetLayout);
    }

    [Fact]
    public void Word_typed_on_the_comma_key_survives()
    {
        var result = _corrector.CorrectPhrase(
            ",usv", Script.Latin, BundledKeyboardMaps.En, ToHebrew);

        Assert.True(result.Changed);
        Assert.Equal("תודה", result.Corrected);
    }

    [Fact]
    public void Hebrew_keystrokes_switch_back_to_latin()
    {
        var result = _corrector.CorrectPhrase(
            "דיןפ", Script.Hebrew, BundledKeyboardMaps.He, ToLatin);

        Assert.True(result.Changed);
        Assert.Equal("ship", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void Known_hebrew_word_is_left_alone()
    {
        var result = _corrector.CorrectPhrase(
            "שלום", Script.Hebrew, BundledKeyboardMaps.He, ToLatin);

        Assert.False(result.Changed);
        Assert.Equal("שלום", result.Corrected);
    }

    [Fact]
    public void Unknown_hebrew_word_is_not_switched_on_naturalness_alone()
    {
        var result = _corrector.CorrectPhrase(
            "מרים", Script.Hebrew, BundledKeyboardMaps.He, ToLatin);

        Assert.False(result.Changed);
        Assert.Equal("מרים", result.Corrected);
    }

    [Fact]
    public void Hebrew_typo_is_fixed_in_place_without_switching_layout()
    {
        var result = _corrector.CorrectPhrase(
            "בוקד", Script.Hebrew, BundledKeyboardMaps.He, ToLatin);

        Assert.True(result.Changed);
        Assert.Equal("בוקר", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Fact]
    public void A_spell_fix_may_not_manufacture_the_dictionary_hit_a_switch_needs()
    {
        var latinOnly = Path.Combine(Path.GetTempPath(), $"lapsus-en2-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(latinOnly, ["aku 900", "hello 800"]);

        try
        {
            var corrector = new LayoutCorrector(
                new SpellChecker([new DictionarySource("en", latinOnly, Script.Latin)]));

            var result = corrector.CorrectPhrase(
                "שלום", Script.Hebrew, BundledKeyboardMaps.He, ToLatin, null, "he");

            Assert.False(result.Changed);
            Assert.Equal("שלום", result.Corrected);
        }
        finally
        {
            File.Delete(latinOnly);
        }
    }

    [Fact]
    public void Impossible_hebrew_switches_without_a_hebrew_dictionary()
    {
        var latinOnly = Path.Combine(Path.GetTempPath(), $"lapsus-en3-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(latinOnly, ["hello 900"]);

        try
        {
            var corrector = new LayoutCorrector(
                new SpellChecker([new DictionarySource("en", latinOnly, Script.Latin)]));

            var typed = LayoutTranscoder.Transcode("moon", BundledKeyboardMaps.En, BundledKeyboardMaps.He);
            Assert.False(Orthography.IsPossibleWord(typed, Script.Hebrew));

            var result = corrector.CorrectPhrase(
                typed, Script.Hebrew, BundledKeyboardMaps.He, ToLatin, null, "he");

            Assert.True(result.Changed);
            Assert.Equal("moon", result.Corrected);
        }
        finally
        {
            File.Delete(latinOnly);
        }
    }

    [Fact]
    public void Offline_phrase_path_offers_the_hebrew_candidate()
    {
        var result = _corrector.CorrectPhrase("akuo");

        Assert.True(result.Changed);
        Assert.Equal("שלום", result.Corrected);
        Assert.Equal(KeyboardLayout.He, result.TargetLayout);
    }

    [Fact]
    public void A_real_english_word_survives_beside_a_wrong_layout_hebrew_one()
    {
        var result = _corrector.CorrectPhrase(
            "akuo hello", Script.Latin, BundledKeyboardMaps.En, ToHebrew);

        Assert.True(result.Changed);
        Assert.Equal("שלום hello", result.Corrected);
    }

    [Fact]
    public void A_word_the_list_lacks_crosses_on_the_stem_under_its_clitic()
    {
        var result = _corrector.CorrectPhrase(
            "uakuo", Script.Latin, BundledKeyboardMaps.En, ToHebrew);

        Assert.True(result.Changed);
        Assert.Equal("ושלום", result.Corrected);
        Assert.Equal(KeyboardLayout.He, result.TargetLayout);
    }

    [Fact]
    public void Clitic_evidence_does_not_carry_a_word_out_of_hebrew()
    {
        var result = _corrector.CorrectPhrase(
            "ושלום", Script.Hebrew, BundledKeyboardMaps.He, ToLatin);

        Assert.False(result.Changed);
        Assert.Equal("ושלום", result.Corrected);
    }
}
