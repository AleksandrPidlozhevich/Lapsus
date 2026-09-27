using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests;

public class PlainLayoutSwitcherTests
{
    private static readonly LayoutSource Latin = new(Script.Latin, "en", BundledKeyboardMaps.En, "en");

    private static readonly LayoutSource Cyrillic = new(Script.Cyrillic, "ru", BundledKeyboardMaps.Ru, "ru");

    private static readonly LayoutCandidate[] EnRu =
    [
        new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en"),
        new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru", "ru")
    ];

    [Fact]
    public void It_is_ready_with_no_dictionary_and_no_model()
    {
        Assert.True(new PlainLayoutSwitcher().IsReady);
    }

    [Fact]
    public void Latin_keystrokes_are_read_as_the_other_layout()
    {
        var result = new PlainLayoutSwitcher().CorrectPhrase("ghbdtn", Latin, [Latin], EnRu);

        Assert.True(result.Changed);
        Assert.Equal("привет", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    [Fact]
    public void The_other_direction_works_the_same_way()
    {
        var result = new PlainLayoutSwitcher().CorrectPhrase("сфк", Cyrillic, [Cyrillic], EnRu);

        Assert.Equal("car", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void A_typo_is_carried_across_untouched()
    {
        var result = new PlainLayoutSwitcher().CorrectPhrase("ghbdtr", Latin, [Latin], EnRu);

        Assert.Equal("привек", result.Corrected);
    }

    [Fact]
    public void A_real_word_is_converted_too_because_nothing_knows_better()
    {
        var result = new PlainLayoutSwitcher().CorrectPhrase("hello", Latin, [Latin], EnRu);

        Assert.True(result.Changed);
        Assert.Equal("руддщ", result.Corrected);
    }

    [Fact]
    public void Whole_phrases_go_through_word_for_word()
    {
        var result = new PlainLayoutSwitcher().CorrectPhrase("ghbdtn vbh", Latin, [Latin], EnRu);

        Assert.Equal("привет мир", result.Corrected);
    }

    [Fact]
    public void The_typed_whitespace_is_left_exactly_as_it_was()
    {
        var result = new PlainLayoutSwitcher().CorrectPhrase(" ghbdtn ", Latin, [Latin], EnRu);

        Assert.Equal(" привет ", result.Corrected);
    }

    [Fact]
    public void The_tie_break_language_picks_the_target()
    {
        LayoutCandidate[] candidates =
        [
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en"),
            new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru", "ru"),
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk")
        ];

        var result = new PlainLayoutSwitcher()
            .CorrectPhrase("ghbdtn", Latin, [Latin], candidates, KeyboardLayout.Uk);

        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
        Assert.Equal("uk", result.TargetLayoutId);
    }

    [Fact]
    public void Another_layout_of_the_same_script_is_not_the_first_answer()
    {
        LayoutCandidate[] cyrillicOnly =
        [
            new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru", "ru"),
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk")
        ];

        var result = new PlainLayoutSwitcher().CorrectPhrase("привет", Cyrillic, [Cyrillic], cyrillicOnly);

        Assert.False(result.Changed);
        Assert.Equal("привет", result.Corrected);
    }

    [Fact]
    public void With_no_candidate_layout_nothing_happens()
    {
        var result = new PlainLayoutSwitcher().CorrectPhrase("ghbdtn", Latin, [Latin], []);

        Assert.False(result.Changed);
    }

    [Fact]
    public void The_hotkey_may_cycle_the_other_layouts()
    {
        Assert.True(new PlainLayoutSwitcher().SupportsLayoutCycle);
    }

    [Fact]
    public void It_claims_no_vocabulary()
    {
        IPhraseCorrector plain = new PlainLayoutSwitcher();

        Assert.False(plain.Knows(Script.Latin));
        Assert.False(plain.Knows(Script.Cyrillic));
    }

    [Fact]
    public void A_mixed_line_pulls_the_wrong_layout_tail_into_the_rest()
    {
        var result = new PlainLayoutSwitcher()
            .CorrectPhrase("привет мир ntrcn", Latin, [Latin, Cyrillic], EnRu);

        Assert.True(result.Changed);
        Assert.Equal("привет мир текст", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_even_split_is_read_as_a_slip_at_the_end(bool activeIsLatin)
    {
        var active = activeIsLatin ? Latin : Cyrillic;

        var result = new PlainLayoutSwitcher()
            .CorrectPhrase("какойто текст rfrjqnj ntrcn", active, [Latin, Cyrillic], EnRu);

        Assert.Equal("какойто текст какойто текст", result.Corrected);
    }

    [Fact]
    public void A_line_in_one_script_still_goes_the_other_way()
    {
        var result = new PlainLayoutSwitcher().CorrectPhrase("hello world", Latin, [Latin], EnRu);

        Assert.Equal("руддщ цщкдв", result.Corrected);
    }

    [Fact]
    public void A_word_on_the_leave_alone_list_is_not_converted()
    {
        var result = new PlainLayoutSwitcher(new WordExceptions(["hello"]))
            .CorrectPhrase("hello", Latin, [Latin], EnRu);

        Assert.False(result.Changed);
        Assert.Equal("hello", result.Corrected);
    }

    [Fact]
    public void Only_the_listed_word_of_a_line_is_spared()
    {
        var result = new PlainLayoutSwitcher(new WordExceptions(["hello"]))
            .CorrectPhrase("hello ghbdtn", Latin, [Latin], EnRu);

        Assert.True(result.Changed);
        Assert.Equal("hello привет", result.Corrected);
    }

    [Fact]
    public void A_hyphenated_entry_is_matched_before_the_word_scanner_splits_it()
    {
        var result = new PlainLayoutSwitcher(new WordExceptions(["we-on"]))
            .CorrectPhrase("we-on ghbdtn", Latin, [Latin], EnRu);

        Assert.Equal("we-on привет", result.Corrected);
    }
}
