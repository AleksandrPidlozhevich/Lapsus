using System.Collections.Generic;
using System.IO;
using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Languages.Arabic;

public sealed class ArabicCorrectionTests : IDisposable
{
    private readonly string _arPath;
    private readonly string _enPath;
    private readonly LayoutCorrector _corrector;

    private static readonly List<LayoutCandidate> ToArabic =
        [new(Script.Arabic, KeyboardLayout.Ar, BundledKeyboardMaps.Ar, "ar")];

    private static readonly List<LayoutCandidate> ToLatin =
        [new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en")];

    public ArabicCorrectionTests()
    {
        _arPath = Path.Combine(Path.GetTempPath(), $"lapsus-ar-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(_arPath,
        [
            "مرحبا 900",
            "شكرا 800",
            "كتاب 700",
            "أنا 600",
            "إلى 500",
            "آخر 400",
            "مدرسة 300"
        ]);

        _enPath = Path.Combine(Path.GetTempPath(), $"lapsus-en-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(_enPath, ["ship 900", "text 800", "hello 700"]);

        var spell = new SpellChecker(
        [
            new DictionarySource("ar", _arPath, Script.Arabic),
            new DictionarySource("en", _enPath, Script.Latin)
        ]);
        _corrector = new LayoutCorrector(spell);
    }

    public void Dispose()
    {
        File.Delete(_arPath);
        File.Delete(_enPath);
    }

    [Fact]
    public void Latin_keystrokes_switch_to_arabic()
    {
        var result = _corrector.CorrectPhrase(
            "lvpfh", Script.Latin, BundledKeyboardMaps.En, ToArabic);

        Assert.True(result.Changed);
        Assert.Equal("مرحبا", result.Corrected);
        Assert.Equal(KeyboardLayout.Ar, result.TargetLayout);
    }

    [Theory]
    [InlineData("Hkh", "أنا")] // أ = Shift+H
    [InlineData("Ygn", "إلى")] // إ = Shift+Y
    [InlineData("Nov", "آخر")] // آ = Shift+N
    public void Shifted_level_letters_are_recovered(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(
            typed, Script.Latin, BundledKeyboardMaps.En, ToArabic);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.Ar, result.TargetLayout);
    }

    [Fact]
    public void Arabic_keystrokes_switch_back_to_latin()
    {
        var result = _corrector.CorrectPhrase(
            "ساهح", Script.Arabic, BundledKeyboardMaps.Ar, ToLatin);

        Assert.True(result.Changed);
        Assert.Equal("ship", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void Shifted_letter_decodes_back_to_a_capital()
    {
        Assert.Equal("H", LayoutTranscoder.Transcode("أ", BundledKeyboardMaps.Ar, BundledKeyboardMaps.En));
        Assert.Equal("أ", LayoutTranscoder.Transcode("H", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));
        Assert.Equal("ا", LayoutTranscoder.Transcode("h", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));
    }

    [Fact]
    public void Known_arabic_word_is_left_alone()
    {
        var result = _corrector.CorrectPhrase(
            "مرحبا", Script.Arabic, BundledKeyboardMaps.Ar, ToLatin);

        Assert.False(result.Changed);
        Assert.Equal("مرحبا", result.Corrected);
    }

    [Fact]
    public void Unknown_arabic_word_is_not_switched_on_naturalness_alone()
    {
        var result = _corrector.CorrectPhrase(
            "قصيدة", Script.Arabic, BundledKeyboardMaps.Ar, ToLatin);

        Assert.False(result.Changed);
        Assert.Equal("قصيدة", result.Corrected);
    }

    [Fact]
    public void The_shifted_level_is_the_one_windows_reports()
    {
        Assert.Equal("لآ", LayoutTranscoder.Transcode("B", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));
        Assert.Equal("لأ", LayoutTranscoder.Transcode("G", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));
        Assert.Equal("لإ", LayoutTranscoder.Transcode("T", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));

        Assert.Equal("،", LayoutTranscoder.Transcode("K", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));
        Assert.Equal("؛", LayoutTranscoder.Transcode("P", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));
        Assert.Equal("؟", LayoutTranscoder.Transcode("?", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));

        Assert.Equal("K", LayoutTranscoder.Transcode("،", BundledKeyboardMaps.Ar, BundledKeyboardMaps.En));
    }

    [Fact]
    public void The_lam_alef_key_types_both_its_letters()
    {
        Assert.Equal("لا", LayoutTranscoder.Transcode("b", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));
        Assert.Equal("لا", LayoutTranscoder.Transcode("gh", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));

        // لا maps back to two keys, not B; landing on "b" would break the hotkey circle.
        Assert.Equal("gh", LayoutTranscoder.Transcode("لا", BundledKeyboardMaps.Ar, BundledKeyboardMaps.En));

        Assert.Equal("خلال", LayoutTranscoder.Transcode("obg", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar));
    }

    [Fact]
    public void Offline_phrase_path_offers_the_arabic_candidate()
    {
        var result = _corrector.CorrectPhrase("lvpfh");

        Assert.True(result.Changed);
        Assert.Equal("مرحبا", result.Corrected);
        Assert.Equal(KeyboardLayout.Ar, result.TargetLayout);
    }

    [Fact]
    public void A_spell_fix_may_not_manufacture_the_dictionary_hit_a_switch_needs()
    {
        var latinOnly = Path.Combine(Path.GetTempPath(), $"lapsus-en2-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(latinOnly, ["lvpf 900", "hello 800"]);

        try
        {
            var corrector = new LayoutCorrector(
                new SpellChecker([new DictionarySource("en", latinOnly, Script.Latin)]));

            var result = corrector.CorrectPhrase(
                "مرحبا", Script.Arabic, BundledKeyboardMaps.Ar, ToLatin, null, "ar");

            Assert.False(result.Changed);
            Assert.Equal("مرحبا", result.Corrected);
        }
        finally
        {
            File.Delete(latinOnly);
        }
    }

    [Fact]
    public void A_real_english_word_survives_beside_a_wrong_layout_arabic_one()
    {
        var result = _corrector.CorrectPhrase(
            "lvpfh hello", Script.Latin, BundledKeyboardMaps.En, ToArabic);

        Assert.True(result.Changed);
        Assert.Equal("مرحبا hello", result.Corrected);
    }

    [Fact]
    public void A_word_the_list_lacks_crosses_on_the_stem_under_its_article()
    {
        var result = _corrector.CorrectPhrase(
            "hg;jhf", Script.Latin, BundledKeyboardMaps.En, ToArabic);

        Assert.True(result.Changed);
        Assert.Equal("الكتاب", result.Corrected);
        Assert.Equal(KeyboardLayout.Ar, result.TargetLayout);
    }

    [Fact]
    public void The_two_spellings_of_a_word_are_one_word_to_the_list()
    {
        var result = KeysOnly("l]vsi");

        Assert.True(result.Changed);

        // Fold is for lookup only; write back the typed spelling.
        Assert.Equal("مدرسه", result.Corrected);
    }

    [Fact]
    public void Alef_maqsura_is_not_folded_to_ya()
    {
        // Alef maqsura is not folded to ya; folding was measured and loses more than it gains.
        var result = KeysOnly("hgd");

        Assert.False(result.Changed);
        Assert.Equal("hgd", result.Corrected);
    }

    private PhraseCorrection KeysOnly(string typed)
    {
        var english = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En, "en-US");
        var arabic = new LayoutSource(Script.Arabic, "ar", BundledKeyboardMaps.Ar, "ar-id");

        return _corrector.CorrectPhrase(
            typed, english, [english, arabic], ToArabic, null, new CorrectionHints(null, true));
    }
}
