using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests.Languages.Greek;

public sealed class GreekCorrectionTests : IDisposable
{
    private readonly ScriptHarness _harness = new();
    private readonly LayoutCorrector _corrector;

    public GreekCorrectionTests()
    {
        _corrector = _harness
            .With("el", Script.Greek, "νερό 200", "καλημέρα 500", "καλημερα 400", "ευχαριστώ 300", "έχω 450")
            .With("en", Script.Latin, "cat 900", "server 700")
            .Corrector();
    }

    public void Dispose()
    {
        _harness.Dispose();
    }

    [Fact]
    public void Latin_keystrokes_switch_to_greek()
    {
        var result = _corrector.CorrectPhrase(
            "kalhmera", Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.El, "el"));

        Assert.True(result.Changed);
        Assert.Equal("καλημερα", result.Corrected);
        Assert.Equal(KeyboardLayout.El, result.TargetLayout);
    }

    [Fact]
    public void Greek_keystrokes_switch_back_to_latin()
    {
        var result = _corrector.CorrectPhrase(
            "ψατ", Script.Greek, Layouts.Map(KeyboardLayout.El), Layouts.ToEnglish);

        Assert.True(result.Changed);
        Assert.Equal("cat", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void Offline_phrase_path_offers_the_greek_candidate()
    {
        var result = _corrector.CorrectPhrase("nero");

        Assert.True(result.Changed);
        Assert.Equal("νερό", result.Corrected);
        Assert.Equal(KeyboardLayout.El, result.TargetLayout);
    }

    [Fact]
    public void A_typo_is_fixed_on_top_of_the_layout_swap()
    {
        var result = _corrector.CorrectPhrase(
            "nero", Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.El, "el"));

        Assert.True(result.Changed);
        Assert.Equal("νερό", result.Corrected);
        Assert.Equal(KeyboardLayout.El, result.TargetLayout);
    }

    [Fact]
    public void A_greek_typo_is_fixed_in_place_without_switching_layout()
    {
        var result = _corrector.CorrectPhrase(
            "νεο", Script.Greek, Layouts.Map(KeyboardLayout.El),
            Layouts.ToOnly(KeyboardLayout.El, "el"));

        Assert.True(result.Changed);
        Assert.Equal("νερό", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Theory]
    [InlineData("καλημέρα")]
    [InlineData("νερό ευχαριστώ")]
    public void Real_greek_is_left_alone(string text)
    {
        var result = _corrector.CorrectPhrase(
            text, Script.Greek, Layouts.Map(KeyboardLayout.El), Layouts.ToEnglish);

        Assert.False(result.Changed);
        Assert.Equal(text, result.Corrected);
    }

    [Fact]
    public void Real_english_is_not_dragged_into_greek()
    {
        var result = _corrector.CorrectPhrase(
            "cat", Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.El, "el"));

        Assert.False(result.Changed);
        Assert.Equal("cat", result.Corrected);
    }

    [Theory]
    [InlineData("kalhm;era", "καλημέρα")]
    [InlineData("eyxarist;v", "ευχαριστώ")]
    [InlineData("ner;o", "νερό")]
    [InlineData("kalhm;era ner;o", "καλημέρα νερό")]
    public void A_word_typed_with_the_tonos_key_switches_whole(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(
            typed, Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.El, "el"));

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.El, result.TargetLayout);
    }

    [Fact]
    public void A_word_opening_with_the_tonos_key_is_one_word()
    {
        var result = _corrector.CorrectPhrase(
            ";exv", Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.El, "el"));

        Assert.Equal("έχω", result.Corrected);
    }

    [Theory]
    [InlineData("server; cat")]
    [InlineData("cat;")]
    public void A_semicolon_that_accents_nothing_stays_punctuation(string text)
    {
        var result = _corrector.CorrectPhrase(
            text, Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.El, "el"));

        Assert.Equal(text, result.Corrected);
    }

    [Fact]
    public void An_accented_greek_word_typed_on_the_greek_layout_meaning_english_comes_back()
    {
        var result = _corrector.CorrectPhrase(
            "ψατ", Script.Greek, Layouts.Map(KeyboardLayout.El), Layouts.ToEnglish);

        Assert.Equal("cat", result.Corrected);
    }

    [Fact]
    public void A_real_english_word_survives_beside_a_wrong_layout_greek_one()
    {
        var result = _corrector.CorrectPhrase(
            "kalhmera cat", Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.El, "el"));

        Assert.True(result.Changed);
        Assert.Equal("καλημερα cat", result.Corrected);
    }
}
