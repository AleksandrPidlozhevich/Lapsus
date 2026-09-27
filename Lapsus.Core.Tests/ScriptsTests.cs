using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests;

public class ScriptsTests
{
    [Theory]
    [InlineData('a', Script.Latin)]
    [InlineData('Z', Script.Latin)]
    [InlineData('я', Script.Cyrillic)]
    [InlineData('і', Script.Cyrillic)]
    [InlineData('ў', Script.Cyrillic)]
    [InlineData('љ', Script.Cyrillic)]
    [InlineData('α', Script.Greek)]
    [InlineData('ω', Script.Greek)]
    [InlineData('ς', Script.Greek)]
    [InlineData('ά', Script.Greek)]
    [InlineData('Ω', Script.Greek)]
    [InlineData('א', Script.Hebrew)]
    [InlineData('ת', Script.Hebrew)]
    [InlineData('ך', Script.Hebrew)]
    [InlineData('ץ', Script.Hebrew)]
    [InlineData('ა', Script.Georgian)]
    [InlineData('ჰ', Script.Georgian)]
    [InlineData('თ', Script.Georgian)]
    [InlineData('ჲ', Script.Georgian)]
    public void Classifies_a_letter(char ch, Script expected)
    {
        Assert.Equal(expected, Scripts.Of(ch));
    }

    [Fact]
    public void Greek_tonos_mark_is_not_a_letter()
    {
        Assert.Null(Scripts.Of('\u0384'));
    }

    [Theory]
    [InlineData('ַ')]
    [InlineData('׃')]
    [InlineData('׳')]
    public void Hebrew_marks_and_punctuation_are_not_letters(char ch)
    {
        Assert.Null(Scripts.Of(ch));
    }

    [Theory]
    [InlineData('Ა')]
    [InlineData('Ⴀ')]
    [InlineData('჻')]
    public void Georgian_outside_mkhedruli_is_not_a_letter(char ch)
    {
        Assert.Null(Scripts.Of(ch));
    }

    [Fact]
    public void Only_hebrew_is_an_abjad()
    {
        Assert.True(Scripts.IsAbjad(Script.Hebrew));
        Assert.False(Scripts.IsAbjad(Script.Latin));
        Assert.False(Scripts.IsAbjad(Script.Cyrillic));
        Assert.False(Scripts.IsAbjad(Script.Greek));
        Assert.False(Scripts.IsAbjad(Script.Georgian));
    }

    [Fact]
    public void Caseless_scripts_are_the_ones_whose_shift_carries_letters()
    {
        Assert.True(Scripts.IsCaseless(Script.Hebrew));
        Assert.True(Scripts.IsCaseless(Script.Arabic));
        Assert.True(Scripts.IsCaseless(Script.Georgian));
        Assert.False(Scripts.IsCaseless(Script.Latin));
        Assert.False(Scripts.IsCaseless(Script.Cyrillic));
        Assert.False(Scripts.IsCaseless(Script.Greek));
    }

    [Theory]
    [InlineData('1')]
    [InlineData(' ')]
    [InlineData('!')]
    public void Non_letters_have_no_script(char ch)
    {
        Assert.Null(Scripts.Of(ch));
    }

    [Theory]
    [InlineData("hello world", Script.Latin)]
    [InlineData("привіт світ", Script.Cyrillic)]
    [InlineData("καλημέρα κόσμε", Script.Greek)]
    [InlineData("שלום עולם", Script.Hebrew)]
    [InlineData("გამარჯობა მსოფლიო", Script.Georgian)]
    public void Picks_the_dominant_script(string text, Script expected)
    {
        Assert.Equal(expected, Scripts.Dominant(text));
    }

    [Fact]
    public void No_letters_means_no_dominant_script()
    {
        Assert.Null(Scripts.Dominant("123 !?"));
    }

    [Theory]
    [InlineData("שָׁלוֹם", Script.Hebrew, "שלום")]
    [InlineData("שלום", Script.Hebrew, "שלום")]
    [InlineData("צה״ל", Script.Hebrew, "צה\"ל")]
    [InlineData("שקל׳", Script.Hebrew, "שקל'")]
    [InlineData("شكراً", Script.Arabic, "شكرا")]
    [InlineData("كِتَاب", Script.Arabic, "كتاب")]
    [InlineData("ـ", Script.Arabic, "")]
    [InlineData("مــرحبا", Script.Arabic, "مرحبا")]
    [InlineData("п’ять", Script.Cyrillic, "п'ять")]
    [InlineData("пʼять", Script.Cyrillic, "п'ять")]
    [InlineData("п'ять", Script.Cyrillic, "п'ять")]
    [InlineData("don’t", Script.Latin, "don’t")]
    [InlineData("καλός", Script.Greek, "καλός")]
    public void Folds_marks_to_what_the_keys_type(string word, Script script, string expected)
    {
        Assert.Equal(expected, Scripts.FoldMarks(word, script));
    }

    [Fact]
    public void Folding_a_plain_word_returns_the_same_instance()
    {
        const string word = "привіт";
        Assert.Same(word, Scripts.FoldMarks(word, Script.Cyrillic));
    }

    [Theory]
    [InlineData("п'ять", Script.Cyrillic, true)]
    [InlineData("здоров'я", Script.Cyrillic, true)]
    [InlineData("'ять", Script.Cyrillic, false)]
    [InlineData("п'", Script.Cyrillic, false)]
    [InlineData("п''ять", Script.Cyrillic, false)]
    [InlineData("привіт", Script.Cyrillic, true)]
    [InlineData("привіт.", Script.Cyrillic, false)]
    [InlineData("צה\"ל", Script.Hebrew, true)]
    [InlineData("וכו'", Script.Hebrew, false)]
    [InlineData("don't", Script.Latin, false)]
    [InlineData("hello", Script.Latin, true)]
    [InlineData("", Script.Latin, false)]
    [InlineData("שָׁלוֹם", Script.Hebrew, true)]
    [InlineData("كِتَاب", Script.Arabic, true)]
    [InlineData("ِكتاب", Script.Arabic, false)]
    [InlineData("ـ", Script.Arabic, false)]
    public void A_word_of_the_script_may_carry_a_mark_only_between_letters(string word, Script script, bool expected)
    {
        Assert.Equal(expected, Scripts.IsWordOf(word, script));
    }
}
