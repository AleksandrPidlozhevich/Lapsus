using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests;

public class OrthographyTests
{
    [Theory]
    [InlineData("שלום")]
    [InlineData("מרים")]
    [InlineData("תודה")]
    [InlineData("ך")]
    [InlineData("פיליפ")]
    [InlineData("טרמפ")]
    [InlineData("פופ")]
    public void Hebrew_words_are_possible(string word)
    {
        Assert.True(Orthography.IsPossibleWord(word, Script.Hebrew));
    }

    [Theory]
    [InlineData("םלוש")]
    [InlineData("ילךד")]
    [InlineData("םה")]
    public void Hebrew_misplaced_forms_are_impossible(string word)
    {
        Assert.False(Orthography.IsPossibleWord(word, Script.Hebrew));
    }

    [Theory]
    [InlineData("مرحبا")]
    [InlineData("مدرسة")]
    [InlineData("إلى")]
    [InlineData("شىء")]
    [InlineData("الشاطىء")]
    public void Arabic_words_are_possible(string word)
    {
        Assert.True(Orthography.IsPossibleWord(word, Script.Arabic));
    }

    [Theory]
    [InlineData("ةرحبا")]
    [InlineData("مىحبا")]
    public void Arabic_misplaced_letters_are_impossible(string word)
    {
        Assert.False(Orthography.IsPossibleWord(word, Script.Arabic));
    }

    [Theory]
    [InlineData("καλημέρα")]
    [InlineData("κόσμος")]
    [InlineData("σήμερα")]
    [InlineData("τελοσ")]
    [InlineData("τησ")]
    public void Greek_words_are_possible(string word)
    {
        Assert.True(Orthography.IsPossibleWord(word, Script.Greek));
    }

    [Theory]
    [InlineData("ςαλ")]
    [InlineData("κόςμος")]
    public void Greek_misplaced_sigma_is_impossible(string word)
    {
        Assert.False(Orthography.IsPossibleWord(word, Script.Greek));
    }

    [Theory]
    [InlineData("привет")]
    [InlineData("объезд")]
    [InlineData("ъгъл")]
    [InlineData("актьор")]
    [InlineData("ў")]
    public void Cyrillic_words_are_possible(string word)
    {
        Assert.True(Orthography.IsPossibleWord(word, Script.Cyrillic));
    }

    [Theory]
    [InlineData("ылн")]
    [InlineData("ьто")]
    [InlineData("отъь")]
    [InlineData("сьыр")]
    public void Cyrillic_misplaced_signs_are_impossible(string word)
    {
        Assert.False(Orthography.IsPossibleWord(word, Script.Cyrillic));
    }

    [Theory]
    [InlineData("Ылн")]
    [InlineData("Ьто")]
    [InlineData("отЪЬ")]
    [InlineData("сЬЫр")]
    public void Cyrillic_rules_are_case_insensitive(string word)
    {
        Assert.False(Orthography.IsPossibleWord(word, Script.Cyrillic));
    }

    [Fact]
    public void Capitalising_a_word_does_not_change_its_verdict()
    {
        Assert.Equal(
            NaturalnessScorer.Score("ыфцф", Script.Cyrillic),
            NaturalnessScorer.Score("Ыфцф", Script.Cyrillic),
            5);

        Assert.True(Orthography.IsPossibleWord("Привет", Script.Cyrillic));
        Assert.True(Orthography.IsPossibleWord("Ъгъл", Script.Cyrillic));
    }

    [Fact]
    public void Greek_keeps_its_rule_for_a_capitalised_word()
    {
        Assert.False(Orthography.IsPossibleWord("Κόςμος", Script.Greek));
        Assert.True(Orthography.IsPossibleWord("ΚΟΣΜΟΣ", Script.Greek));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("größe")]
    [InlineData("źdźbło")]
    [InlineData("tiếng")]
    public void Latin_has_no_rules(string word)
    {
        Assert.True(Orthography.IsPossibleWord(word, Script.Latin));
    }

    [Fact]
    public void Each_run_of_letters_is_judged_on_its_own()
    {
        Assert.True(Orthography.IsPossibleWord("שלום-עולם", Script.Hebrew));

        Assert.False(Orthography.IsPossibleWord("שלום-םולע", Script.Hebrew));
    }

    [Fact]
    public void Impossible_text_scores_far_below_a_plausible_word()
    {
        var plausible = NaturalnessScorer.Score("сыр", Script.Cyrillic);
        var impossible = NaturalnessScorer.Score("ыср", Script.Cyrillic);

        Assert.True(impossible < plausible,
            $"impossible {impossible:F3} should be below plausible {plausible:F3}");
        Assert.True(impossible <= 0.05);
    }

    [Fact]
    public void Impossible_abjad_text_loses_its_neutral_score()
    {
        var neutral = NaturalnessScorer.Score("שלום", Script.Hebrew);
        var impossible = NaturalnessScorer.Score("ילךד", Script.Hebrew);

        Assert.True(impossible < neutral);
    }
}
