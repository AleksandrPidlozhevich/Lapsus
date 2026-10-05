using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

public sealed class WordExceptionsTests : IDisposable
{
    private readonly string _enPath;
    private readonly string _ruPath;

    private static readonly List<LayoutCandidate> ToCyrillic =
        [new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru", "ru-id")];

    public WordExceptionsTests()
    {
        _enPath = WriteTemp("en", "car 900", "cat 800", "hello 700");
        _ruPath = WriteTemp("ru", "привет 900", "молоко 800");
    }

    public void Dispose()
    {
        File.Delete(_enPath);
        File.Delete(_ruPath);
    }

    [Fact]
    public void An_excluded_word_is_not_switched()
    {
        var exceptions = new WordExceptions(["сфк"]);
        var corrector = Build(exceptions);

        var result = corrector.CorrectPhrase("сфк", Script.Cyrillic, BundledKeyboardMaps.Ru, ToLatin);

        Assert.False(result.Changed);
        Assert.Equal("сфк", result.Corrected);
    }

    [Fact]
    public void The_same_word_is_switched_when_it_is_not_excluded()
    {
        var result = Build(new WordExceptions())
            .CorrectPhrase("сфк", Script.Cyrillic, BundledKeyboardMaps.Ru, ToLatin);

        Assert.True(result.Changed);
        Assert.Equal("car", result.Corrected);
    }

    [Fact]
    public void Exclusion_also_stops_a_typo_fix()
    {
        var corrector = Build(new WordExceptions(["молок"]));

        var result = corrector.CorrectPhrase("молок", Script.Cyrillic, BundledKeyboardMaps.Ru, ToCyrillic);

        Assert.False(result.Changed);
        Assert.Equal("молок", result.Corrected);
    }

    [Fact]
    public void Only_the_excluded_word_of_a_phrase_is_spared()
    {
        var result = Build(new WordExceptions(["сфк"]))
            .CorrectPhrase("сфк сфe", Script.Cyrillic, BundledKeyboardMaps.Ru, ToLatin);

        Assert.True(result.Changed);
        Assert.StartsWith("сфк ", result.Corrected);
        Assert.NotEqual("сфк сфe", result.Corrected);
    }

    [Theory]
    [InlineData("Сфк")]
    [InlineData("сфк,")]
    [InlineData("«сфк»")]
    public void Matching_ignores_case_and_surrounding_punctuation(string typed)
    {
        var result = Build(new WordExceptions(["сфк"]))
            .CorrectPhrase(typed, Script.Cyrillic, BundledKeyboardMaps.Ru, ToLatin);

        Assert.False(result.Changed);
    }

    [Fact]
    public void A_hyphenated_entry_is_matched_before_the_word_scanner_splits_it()
    {
        // "-" is a letter on no installed layout, so the scanner splits "сфк-сфе" into two words.
        var result = Build(new WordExceptions(["сфк-сфе"]))
            .CorrectPhrase("сфк-сфе", Script.Cyrillic, BundledKeyboardMaps.Ru, ToLatin);

        Assert.False(result.Changed);
        Assert.Equal("сфк-сфе", result.Corrected);
    }

    [Fact]
    public void The_same_hyphenated_text_is_corrected_when_it_is_not_listed()
    {
        var result = Build(new WordExceptions())
            .CorrectPhrase("сфк-сфе", Script.Cyrillic, BundledKeyboardMaps.Ru, ToLatin);

        Assert.Equal("car-cat", result.Corrected);
    }

    [Fact]
    public void An_entry_matching_half_a_chunk_still_spares_its_own_token()
    {
        var result = Build(new WordExceptions(["сфк"]))
            .CorrectPhrase("сфк-сфе", Script.Cyrillic, BundledKeyboardMaps.Ru, ToLatin);

        Assert.Equal("сфк-cat", result.Corrected);
    }

    [Fact]
    public void Adding_and_removing_reports_whether_anything_changed()
    {
        var exceptions = new WordExceptions();
        var changes = 0;
        exceptions.Changed += (_, _) => changes++;

        Assert.True(exceptions.Add("Сфк"));
        Assert.False(exceptions.Add("сфк"));
        Assert.True(exceptions.Contains("сфк,"));
        Assert.True(exceptions.Remove("СФК"));
        Assert.False(exceptions.Remove("сфк"));

        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_word_with_no_letters_is_not_remembered()
    {
        var exceptions = new WordExceptions();

        Assert.False(exceptions.Add("123"));
        Assert.False(exceptions.Add("!!!"));
        Assert.Empty(exceptions.Snapshot());
    }

    [Fact]
    public void The_list_survives_a_rebuilt_brain()
    {
        var exceptions = new WordExceptions();
        var before = Build(exceptions);
        exceptions.Add("сфк");
        var after = Build(exceptions);

        Assert.False(before.CorrectPhrase("сфк", Script.Cyrillic, BundledKeyboardMaps.Ru, ToLatin).Changed);
        Assert.False(after.CorrectPhrase("сфк", Script.Cyrillic, BundledKeyboardMaps.Ru, ToLatin).Changed);
    }

    private static readonly List<LayoutCandidate> ToLatin =
        [new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en-id")];

    private LayoutCorrector Build(WordExceptions exceptions)
    {
        return new LayoutCorrector(
            new SpellChecker(
            [
                new DictionarySource("en", _enPath, Script.Latin),
                new DictionarySource("ru", _ruPath, Script.Cyrillic)
            ]),
            exceptions: exceptions);
    }

    private static string WriteTemp(string tag, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{tag}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }
}
