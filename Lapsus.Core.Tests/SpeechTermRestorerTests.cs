using Lapsus.Core.Layout;
using Lapsus.Core.Speech;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

public sealed class SpeechTermRestorerTests : IDisposable
{
    private readonly List<string> _files = [];

    // Counts shaped like the real lists: a common word near the top, зум far down (rarer than RareWordFrequency).
    private static readonly string[] Russian =
    [
        "и 1000000", "в 900000", "на 800000", "а 700000", "нет 600000", "я 500000", "это 400000", "после 90000",
        "мастер 100000", "компьютер 60000", "файл 40000", "сервер 30000", "открой 20000", "сделай 20000",
        "закинул 5000", "там 300000", "конфликт 8000", "ждёт 9000", "массив 3000", "возвращает 4000",
        "этот 200000", "запусти 2000", "напиши 9000", "зум 50", "обнови 3000", "конфиг 500", "много 80000",
        "не 950000", "было 300000", "тоже 200000", "задеплоил 1"
    ];

    private static readonly string[] English =
    [
        "the 5000000", "pull 89880", "request 22533", "merge 1968", "master 95131", "deploy 1874",
        "staging 1379", "commit 15921", "slack 3515", "cut 50000", "me 900000", "some 300000"
    ];

    private SpellChecker Spell(bool withRussian = true)
    {
        var sources = new List<DictionarySource> { new("en", Write(English), Script.Latin) };
        if (withRussian)
            sources.Add(new DictionarySource("ru", Write(Russian), Script.Cyrillic));
        return new SpellChecker(sources);
    }

    private string Write(string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-speech-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _files)
            File.Delete(file);
    }

    [Theory]
    [InlineData("Закинул пулл реквест", "Закинул pull request")]
    [InlineData("Закинул пулреквест в гитхаб", "Закинул pull request в GitHub")]
    [InlineData("Сделай ребейс, там конфликт", "Сделай rebase, там конфликт")]
    [InlineData("Мердж после код ревью", "Merge после code review")]
    [InlineData("Это хотфикс", "Это hotfix")]
    [InlineData("Обнови ридми и конфиг кубернетис", "Обнови README и конфиг Kubernetes")]
    [InlineData("Нужен код-ревью и си-ай", "Нужен code review и CI")]
    [InlineData("Запусти дотнет", "Запусти .NET")]
    [InlineData("Созвон в зум", "Созвон в Zoom")]
    public void Puts_spoken_terms_back_into_latin(string heard, string expected)
    {
        Assert.Equal(expected, new TermRestorer(Spell()).Restore(heard, "ru"));
    }

    [Fact]
    public void Finds_ordinary_english_words_in_the_frequency_list()
    {
        var restorer = new TermRestorer(Spell());

        Assert.Equal("Сделай merge на master", restorer.Restore("Сделай мердж на мэстер", "ru"));
        Assert.Equal("Это staging", restorer.Restore("Это стейджинг", "ru"));
    }

    [Theory]
    [InlineData("Компьютер завис, открой файл и сервер")]
    [InlineData("Сделай это на мастер")]
    [InlineData("Ребейса не было, деплоя тоже")]
    [InlineData("Я задеплоил это, коммитов много")]
    [InlineData("У нас нет")]
    public void Leaves_real_words_and_inflected_terms_alone(string heard)
    {
        Assert.Equal(heard, new TermRestorer(Spell()).Restore(heard, "ru"));
    }

    [Fact]
    public void Never_swallows_a_short_word_into_a_term()
    {
        Assert.Equal("Этот endpoint возвращает массив, а frontend ждёт",
            new TermRestorer(Spell()).Restore("Этот эндпоинт возвращает массив, а фронтенд ждёт", "ru"));
    }

    [Fact]
    public void The_glossary_wins_even_over_a_known_word()
    {
        var restorer = new TermRestorer(Spell(), ["Master Plan|мастер план", "Lapsus|лапсус"]);

        Assert.Equal("Открой Master Plan в Lapsus", restorer.Restore("Открой мастер план в лапсус", "ru"));
    }

    [Fact]
    public void Fixes_the_spelling_of_a_latin_term_but_not_english_words()
    {
        var restorer = new TermRestorer(Spell());

        // Short and Latin-heavy, yet Russian: the language decides, not the letter count.
        Assert.Equal("Напиши в Slack, что Hotfix в GitHub",
            restorer.Restore("Напиши в slack, что Hatfix в github", "ru"));
        Assert.Equal("Cut me some slack", restorer.Restore("Cut me some slack", "en"));
        Assert.Equal("Open the github page", restorer.Restore("Open the github page", "en"));
    }

    [Fact]
    public void Without_a_dictionary_for_the_language_only_listed_terms_apply()
    {
        var restorer = new TermRestorer(Spell(withRussian: false));

        Assert.Equal("Закинул pull request, мэстер", restorer.Restore("Закинул пулл реквест, мэстер", "ru"));
    }

    [Theory]
    [InlineData("request", "реквест")]
    [InlineData("merge", "мердж")]
    [InlineData("rebase", "ребейс")]
    [InlineData("hotfix", "хотфикс")]
    [InlineData("deploy", "деплой")]
    [InlineData("pull", "пулл")]
    [InlineData("docker", "докер")]
    public void An_english_word_and_its_cyrillic_spelling_sound_alike(string english, string heard)
    {
        Assert.Equal(PhoneticKey.English(english), PhoneticKey.Heard(heard));
    }
}
