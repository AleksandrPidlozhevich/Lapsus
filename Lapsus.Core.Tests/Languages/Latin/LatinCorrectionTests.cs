using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests.Languages.Latin;

public sealed class LatinCorrectionTests : IDisposable
{
    private readonly ScriptHarness _harness = new();
    private readonly LayoutCorrector _corrector;

    public LatinCorrectionTests()
    {
        _corrector = _harness
            .With("en", Script.Latin, "car 900", "cat 850", "milk 700", "with 950", "server 600")
            .With("uk", Script.Cyrillic, "кава 900", "молоко 800", "вітання 850")
            .Corrector();
    }

    public void Dispose()
    {
        _harness.Dispose();
    }

    [Theory]
    [InlineData("сфк", "car")]
    [InlineData("сфе", "cat")]
    [InlineData("ьшдл", "milk")]
    public void Cyrillic_keystrokes_switch_to_latin(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(
            typed, Script.Cyrillic, Layouts.Map(KeyboardLayout.Uk), Layouts.ToEnglish);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void Latin_keystrokes_switch_back_to_cyrillic()
    {
        var result = _corrector.CorrectPhrase(
            "rfdf", Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.Uk, "uk"));

        Assert.True(result.Changed);
        Assert.Equal("кава", result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
    }

    [Fact]
    public void Offline_phrase_path_offers_the_latin_candidate()
    {
        var result = _corrector.CorrectPhrase("сфк");

        Assert.True(result.Changed);
        Assert.Equal("car", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void A_typo_is_fixed_on_top_of_the_layout_swap()
    {
        var result = _corrector.CorrectPhrase(
            "ьшдд", Script.Cyrillic, Layouts.Map(KeyboardLayout.Uk), Layouts.ToEnglish);

        Assert.True(result.Changed);
        Assert.Equal("milk", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void An_english_typo_is_fixed_in_place_without_switching_layout()
    {
        var result = _corrector.CorrectPhrase(
            "mikl", Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.Uk, "uk"));

        Assert.True(result.Changed);
        Assert.Equal("milk", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Theory]
    [InlineData("car")]
    [InlineData("cat with milk")]
    public void Real_english_is_left_alone(string text)
    {
        var result = _corrector.CorrectPhrase(
            text, Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.Uk, "uk"));

        Assert.False(result.Changed);
        Assert.Equal(text, result.Corrected);
    }

    [Fact]
    public void Real_ukrainian_is_not_dragged_into_latin()
    {
        var result = _corrector.CorrectPhrase(
            "кава", Script.Cyrillic, Layouts.Map(KeyboardLayout.Uk), Layouts.ToEnglish);

        Assert.False(result.Changed);
        Assert.Equal("кава", result.Corrected);
    }

    [Fact]
    public void A_real_english_word_survives_a_wrong_layout_run_beside_it()
    {
        var result = _corrector.CorrectPhrase(
            "сфк сфе кава", Script.Cyrillic, Layouts.Map(KeyboardLayout.Uk), Layouts.ToEnglish);

        Assert.True(result.Changed);
        Assert.Equal("car cat кава", result.Corrected);
    }
}
