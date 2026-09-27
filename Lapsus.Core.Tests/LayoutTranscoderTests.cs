using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests;

public class LayoutTranscoderTests
{
    private const string EnKeys = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
    private const string UkKeys = "'йцукенгшщзхїфівапролджєячсмитьбю.";

    private static readonly KeyboardMap En = new(EnKeys.ToCharArray());
    private static readonly KeyboardMap Uk = new(UkKeys.ToCharArray());

    [Fact]
    public void Transcodes_a_phrase_en_to_ukrainian()
    {
        Assert.Equal("привіт світ", LayoutTranscoder.Transcode("ghbdsn cdsn", En, Uk));
    }

    [Fact]
    public void Transcodes_back_to_latin()
    {
        Assert.Equal("ghbdsn", LayoutTranscoder.Transcode("привіт", Uk, En));
    }

    [Fact]
    public void Preserves_case()
    {
        Assert.Equal("Привіт", LayoutTranscoder.Transcode("Ghbdsn", En, Uk));
    }

    [Fact]
    public void Characters_absent_on_the_source_pass_through()
    {
        Assert.Equal("Tesla", LayoutTranscoder.Transcode("Tesla", Uk, En));
    }

    [Fact]
    public void Digits_and_spaces_survive()
    {
        Assert.Equal("привіт 42", LayoutTranscoder.Transcode("ghbdsn 42", En, Uk));
    }

    [Fact]
    public void Shifted_punctuation_of_a_foreign_layout_is_left_alone()
    {
        var shifted = new char[EnKeys.Length];
        shifted[EnKeys.IndexOf(';')] = ':';
        var declared = new KeyboardMap(EnKeys.ToCharArray(), shifted);

        Assert.Equal(":", LayoutTranscoder.Transcode(":", declared, En));

        Assert.Equal("Привіт", LayoutTranscoder.Transcode("Ghbdsn", En, Uk));
    }

    [Fact]
    public void Round_trip_is_identity()
    {
        var there = LayoutTranscoder.Transcode("Hello", En, Uk);
        Assert.Equal("Hello", LayoutTranscoder.Transcode(there, Uk, En));
    }

    [Fact]
    public void Two_source_maps_re_type_both_halves_of_a_line()
    {
        Assert.Equal("привіт текст", LayoutTranscoder.Transcode("привіт ntrcn", [Uk, En], Uk));
        Assert.Equal("ghbdsn ntrcn", LayoutTranscoder.Transcode("привіт ntrcn", [Uk, En], En));

        Assert.Equal("привіт ntrcn", LayoutTranscoder.Transcode("привіт ntrcn", Uk, Uk));
    }

    [Fact]
    public void The_first_map_claims_a_character_they_share()
    {
        Assert.Equal("-", LayoutTranscoder.Transcode("-", [Uk, En], Uk));
        Assert.Equal("Привіт", LayoutTranscoder.Transcode("Ghbdsn", [En, Uk], Uk));
    }

    [Fact]
    public void A_word_already_in_the_target_script_is_not_re_encoded_into_another_of_its_layouts()
    {
        var sourceMaps = new[] { BundledKeyboardMaps.Uk, BundledKeyboardMaps.En };

        Assert.Equal("привыт мир текст", LayoutTranscoder.Transcode(
            "привіт мир ntrcn", sourceMaps, BundledKeyboardMaps.Ru));

        Assert.Equal("привіт мир текст", LayoutTranscoder.TranscodeIntoScript(
            "привіт мир ntrcn", sourceMaps, BundledKeyboardMaps.Ru, Script.Cyrillic));
    }

    [Fact]
    public void A_line_of_one_script_is_still_transcoded_whole()
    {
        var sourceMaps = new[] { BundledKeyboardMaps.En };

        Assert.Equal("руддщ цщкдв", LayoutTranscoder.TranscodeIntoScript(
            "hello world", sourceMaps, BundledKeyboardMaps.Ru, Script.Cyrillic));
    }

    [Fact]
    public void A_kept_span_is_left_exactly_as_typed()
    {
        var sourceMaps = new[] { BundledKeyboardMaps.Uk, BundledKeyboardMaps.En };

        Assert.Equal("привіт cat", LayoutTranscoder.TranscodeIntoScript(
            "ghbdsn cat", sourceMaps, BundledKeyboardMaps.Uk, Script.Cyrillic, [new TextSpan(7, 3)]));
    }

    [Fact]
    public void A_kept_span_survives_a_line_of_one_script()
    {
        var sourceMaps = new[] { BundledKeyboardMaps.En };

        Assert.Equal("руддщ cat", LayoutTranscoder.TranscodeIntoScript(
            "hello cat", sourceMaps, BundledKeyboardMaps.Ru, Script.Cyrillic, [new TextSpan(6, 3)]));
    }

    [Fact]
    public void With_nothing_kept_it_matches_the_plain_overload()
    {
        Assert.Equal("привіт мир текст", LayoutTranscoder.TranscodeIntoScript(
            "привіт мир ntrcn", [BundledKeyboardMaps.Uk, BundledKeyboardMaps.En],
            BundledKeyboardMaps.Ru, Script.Cyrillic, []));

        Assert.Equal("руддщ цщкдв", LayoutTranscoder.TranscodeIntoScript(
            "hello world", [BundledKeyboardMaps.En], BundledKeyboardMaps.Ru, Script.Cyrillic, []));
    }

    [Fact]
    public void Spans_outside_the_line_are_clipped_not_thrown()
    {
        // Built on the keyboard-hook thread, where a throw would unwind into the OS hook.
        Assert.Equal("руддщ", LayoutTranscoder.TranscodeIntoScript(
            "hello", [BundledKeyboardMaps.En], BundledKeyboardMaps.Ru, Script.Cyrillic,
            [new TextSpan(-4, 3), new TextSpan(90, 5), new TextSpan(2, -1)]));
    }

    [Theory]
    [InlineData("kal;ow", "καλός")]
    [InlineData("kalhm;era", "καλημέρα")]
    [InlineData("eyxarist;v", "ευχαριστώ")]
    [InlineData(";exv", "έχω")]
    [InlineData("K;alow", "Κάλος")]
    [InlineData("KAL;OS", "ΚΑΛΌΣ")]
    [InlineData(";Exv", "Έχω")]
    [InlineData("pro:ion", "προϊον")]
    [InlineData("Wi", "ΐ")]
    public void A_dead_key_and_the_letter_after_it_make_one_greek_letter(string keys, string greek)
    {
        Assert.Equal(greek, LayoutTranscoder.Transcode(keys, BundledKeyboardMaps.En, BundledKeyboardMaps.El));
        Assert.Equal(keys, LayoutTranscoder.Transcode(greek, BundledKeyboardMaps.El, BundledKeyboardMaps.En));
    }

    [Theory]
    [InlineData("ti;", "τι;")]
    [InlineData("a; b", "α; β")]
    [InlineData(";1", ";1")]
    [InlineData(";k", ";κ")]
    public void A_dead_key_before_anything_it_cannot_accent_stays_a_key(string keys, string greek)
    {
        Assert.Equal(greek, LayoutTranscoder.Transcode(keys, BundledKeyboardMaps.En, BundledKeyboardMaps.El));
    }

    [Fact]
    public void An_accented_greek_letter_is_its_two_keys_on_any_other_layout()
    {
        Assert.Equal("лфджщц", LayoutTranscoder.Transcode("καλός", BundledKeyboardMaps.El, BundledKeyboardMaps.Ru));
        Assert.Equal("καλός", LayoutTranscoder.Transcode("καλός", BundledKeyboardMaps.El, BundledKeyboardMaps.El));
    }

    [Fact]
    public void A_kept_span_never_composes_with_the_key_beside_it()
    {
        Assert.Equal("kal;ος", LayoutTranscoder.TranscodeIntoScript(
            "kal;ow", [BundledKeyboardMaps.En], BundledKeyboardMaps.El, Script.Greek, [new TextSpan(0, 4)]));
    }

    [Fact]
    public void Several_source_maps_still_find_the_dead_key()
    {
        Assert.Equal("τι κάνεις", LayoutTranscoder.Transcode(
            "τι k;aneiw", [BundledKeyboardMaps.En, BundledKeyboardMaps.El], BundledKeyboardMaps.El));
    }

    [Fact]
    public void Covering_every_letter_ignores_digits_and_punctuation()
    {
        Assert.True(TextSpans.CoversEveryLetter("cat 42!", [new TextSpan(0, 3)]));
        Assert.False(TextSpans.CoversEveryLetter("cat dog", [new TextSpan(0, 3)]));

        Assert.False(TextSpans.CoversEveryLetter("42", [new TextSpan(0, 2)]));
    }
}
