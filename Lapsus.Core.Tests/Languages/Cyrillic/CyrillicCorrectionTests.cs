using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests.Languages.Cyrillic;

public sealed class CyrillicCorrectionTests : IDisposable
{
    private readonly ScriptHarness _harness = new();
    private readonly LayoutCorrector _corrector;

    public CyrillicCorrectionTests()
    {
        _corrector = _harness
            .With("uk", Script.Cyrillic,
                "кава 900", "вітання 850", "молоко 800", "дякую 700", "п'ять 600", "я 950", "здоров 300", "здорово 250")
            .With("ru", Script.Cyrillic, "привет 900", "кофе 800", "молоко 800", "спасибо 700")
            .With("en", Script.Latin, "car 900", "cat 850", "server 700")
            .Corrector();
    }

    public void Dispose()
    {
        _harness.Dispose();
    }

    [Theory]
    [InlineData("rfdf", "кава")]
    [InlineData("dsnfyyz", "вітання")]
    public void Latin_keystrokes_switch_to_ukrainian(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(
            typed, Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.Uk, "uk"));

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
    }

    [Fact]
    public void Cyrillic_keystrokes_switch_back_to_latin()
    {
        var result = _corrector.CorrectPhrase(
            "сфк", Script.Cyrillic, Layouts.Map(KeyboardLayout.Uk), Layouts.ToEnglish);

        Assert.True(result.Changed);
        Assert.Equal("car", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void Offline_phrase_path_offers_a_cyrillic_candidate()
    {
        var result = _corrector.CorrectPhrase("rfdf");

        Assert.True(result.Changed);
        Assert.Equal("кава", result.Corrected);
    }

    [Fact]
    public void A_typo_is_fixed_on_top_of_the_layout_swap()
    {
        var result = _corrector.CorrectPhrase(
            "dsnfyyx", Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.Uk, "uk"));

        Assert.True(result.Changed);
        Assert.Equal("вітання", result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
    }

    [Fact]
    public void A_cyrillic_typo_is_fixed_in_place_without_switching_layout()
    {
        var result = _corrector.CorrectPhrase(
            "кафа", Script.Cyrillic, Layouts.Map(KeyboardLayout.Uk), Layouts.ToEnglish);

        Assert.True(result.Changed);
        Assert.Equal("кава", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Theory]
    [InlineData("кава")]
    [InlineData("вітання кава")]
    public void Real_ukrainian_is_left_alone(string text)
    {
        var result = _corrector.CorrectPhrase(
            text, Script.Cyrillic, Layouts.Map(KeyboardLayout.Uk), Layouts.ToEnglish);

        Assert.False(result.Changed);
        Assert.Equal(text, result.Corrected);
    }

    [Fact]
    public void Real_english_is_not_dragged_into_cyrillic()
    {
        var result = _corrector.CorrectPhrase(
            "car", Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.Uk, "uk"));

        Assert.False(result.Changed);
        Assert.Equal("car", result.Corrected);
    }

    [Theory]
    [InlineData("dsnfyyz", "вітання", KeyboardLayout.Uk)]
    [InlineData("ghbdtn", "привет", KeyboardLayout.Ru)]
    public void The_language_that_owns_the_word_wins_the_layout(
        string typed, string expected, KeyboardLayout expectedLayout)
    {
        var result = _corrector.CorrectPhrase(
            typed, Script.Latin, Layouts.Map(KeyboardLayout.En),
            [Layouts.To(KeyboardLayout.Ru, "ru"), Layouts.To(KeyboardLayout.Uk, "uk")]);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(expectedLayout, result.TargetLayout);
    }

    [Theory]
    [InlineData(KeyboardLayout.Uk)]
    [InlineData(KeyboardLayout.Ru)]
    public void A_word_both_languages_know_follows_the_preferred_layout(KeyboardLayout preferred)
    {
        var result = _corrector.CorrectPhrase(
            "vjkjrj", Script.Latin, Layouts.Map(KeyboardLayout.En),
            [Layouts.To(KeyboardLayout.Ru, "ru"), Layouts.To(KeyboardLayout.Uk, "uk")],
            preferred);

        Assert.True(result.Changed);
        Assert.Equal("молоко", result.Corrected);
        Assert.Equal(preferred, result.TargetLayout);
    }

    [Theory]
    [InlineData("g`znm", "п'ять")]
    [InlineData("pljhjd`z", "здоров'я")]
    [InlineData("g`znm rfdf", "п'ять кава")]
    public void The_apostrophe_inside_a_ukrainian_word_travels_with_it(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(
            typed, Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.Uk, "uk"));

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
    }

    [Theory]
    [InlineData("п'ять")]
    [InlineData("п'ять кава")]
    public void A_ukrainian_word_with_an_apostrophe_typed_right_stays_whole(string text)
    {
        var result = _corrector.CorrectPhrase(
            text, Script.Cyrillic, Layouts.Map(KeyboardLayout.Uk), Layouts.ToEnglish, null, "uk");

        Assert.False(result.Changed);
        Assert.Equal(text, result.Corrected);
        Assert.Contains(result.Settled ?? [], span => span.Length == "п'ять".Length);
    }

    [Fact]
    public void An_english_contraction_keeps_its_apostrophe_and_is_not_split()
    {
        var result = _corrector.CorrectPhrase(
            "car's", Script.Latin, Layouts.Map(KeyboardLayout.En), Layouts.ToOnly(KeyboardLayout.Uk, "uk"));

        Assert.Equal("car's", result.Corrected);
    }

    [Fact]
    public void A_real_english_word_survives_a_wrong_layout_run_beside_it()
    {
        var result = _corrector.CorrectPhrase(
            "dsnfyyz rfdf server", Script.Latin, Layouts.Map(KeyboardLayout.En),
            Layouts.ToOnly(KeyboardLayout.Uk, "uk"));

        Assert.True(result.Changed);
        Assert.Equal("вітання кава server", result.Corrected);
    }
}
