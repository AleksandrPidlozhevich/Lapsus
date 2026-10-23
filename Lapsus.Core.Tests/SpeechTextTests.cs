using Lapsus.Core.Speech;

namespace Lapsus.Core.Tests;

public class SpeechTextTests
{
    [Theory]
    [InlineData("Субтитры создавал DimaTorzok")]
    [InlineData("Продолжение следует...")]
    [InlineData("Редактор субтитров А.Семкин Корректор А.Егорова")]
    [InlineData("Thank you for watching!")]
    [InlineData("   ")]
    public void Drops_what_whisper_writes_over_silence(string raw)
    {
        Assert.Equal("", HallucinationFilter.Clean(raw));
    }

    [Fact]
    public void Drops_a_credit_line_after_real_speech()
    {
        Assert.Equal("Буду через десять минут.",
            HallucinationFilter.Clean("Буду через десять минут. Продолжение следует..."));
    }

    [Fact]
    public void Collapses_a_looping_sentence_and_its_cut_off_copy()
    {
        var loop = "Спасибо за помощь, вечером напишу подробнее. Спасибо за помощь, вечером напишу подробнее. " +
                   "Спасибо за помощь, вечером напишу подробнее. Спасибо за помощь, вечером";

        Assert.Equal("Спасибо за помощь, вечером напишу подробнее.", HallucinationFilter.Clean(loop));
    }

    [Fact]
    public void Collapses_a_repeated_run_of_words_inside_a_sentence()
    {
        Assert.Equal("Открой файл и закрой",
            HallucinationFilter.Clean("Открой файл и открой файл и открой файл и закрой"));
    }

    [Theory]
    [InlineData("Нет, нет, нет, я не согласен.")]
    [InlineData("Привет, я буду минут через десять, начинайте без меня.")]
    [InlineData("Закинул pull request в GitHub. Посмотри, когда будет время.")]
    public void Leaves_ordinary_speech_alone(string text)
    {
        Assert.Equal(text, HallucinationFilter.Clean(text));
    }

    [Fact]
    public void The_prompt_shows_the_language_mixing_with_english_then_glossary_then_earlier_text()
    {
        var prompt = SpeechPromptBuilder.Build("ru-RU", ["Lapsus", "Avalonia"], "Я уже написал первую часть.");

        Assert.StartsWith(SpeechPromptBuilder.SampleFor("ru")!, prompt);
        Assert.Contains("pull request", prompt);
        Assert.True(prompt.IndexOf("Lapsus, Avalonia.", StringComparison.Ordinal) <
                    prompt.IndexOf("Я уже написал", StringComparison.Ordinal));
        Assert.EndsWith("Я уже написал первую часть.", prompt);
    }

    [Fact]
    public void The_prompt_stays_within_whisper_budget()
    {
        var glossary = Enumerable.Range(0, 200).Select(i => $"Term{i}");
        var previous = string.Join(' ', Enumerable.Repeat("слово", 200));

        var prompt = SpeechPromptBuilder.Build("uk", glossary, previous);

        Assert.True(prompt.Length <= SpeechPromptBuilder.MaxChars, $"{prompt.Length} chars");
        Assert.StartsWith(SpeechPromptBuilder.SampleFor("uk")!, prompt);
        Assert.EndsWith("слово", prompt);
    }

    [Fact]
    public void A_language_without_a_sample_still_gets_the_glossary()
    {
        Assert.Equal("Lapsus.", SpeechPromptBuilder.Build("tr", ["Lapsus"]));
    }

    [Fact]
    public void Detection_chooses_only_among_the_user_languages_main_first()
    {
        Assert.Equal(["ru", "uk", "en"],
            DictationLanguagePicker.Candidates("ru-RU", ["uk-UA", "en-US", "ru", "xx"]));
        Assert.Equal(["de"], DictationLanguagePicker.Candidates("ru", ["en"], fixedLanguage: "de"));
        Assert.Equal(["en"], DictationLanguagePicker.Candidates("xx", []));
    }

    [Theory]
    [InlineData("en", 0.5f, "ru")]
    [InlineData("en", 0.9f, "en")]
    [InlineData("ru", 0.2f, "ru")]
    [InlineData(null, 0f, "ru")]
    public void Leaving_the_main_language_takes_a_confident_detection(string? detected, float p, string expected)
    {
        Assert.Equal(expected, DictationLanguagePicker.Resolve("ru", detected, p));
    }
}
