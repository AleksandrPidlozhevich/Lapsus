using Lapsus.Core.Layout;
using Lapsus.Core.Text;

namespace Lapsus.Core.Tests;

public sealed class AbjadTransliterationTests
{
    [Theory]
    [InlineData("shalom", "שלום")]
    [InlineData("ani", "אני")]
    [InlineData("toda", "טודה")] // t is ט, not ת — Latin cannot choose.
    [InlineData("kfar", "כפר")]
    public void Latin_becomes_hebrew(string typed, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(typed, Script.Hebrew));
    }

    [Fact]
    public void A_word_ends_on_the_final_form_of_its_letter()
    {
        Assert.Equal("שלום", Transliterator.Convert("shalom", Script.Hebrew));
        Assert.Equal("שלום שלום", Transliterator.Convert("shalom shalom", Script.Hebrew));

        Assert.Equal("מים", Transliterator.Convert("mim", Script.Hebrew));
    }

    [Theory]
    [InlineData("mar7aba", "مرحبا")]
    [InlineData("shukran", "شوكرن")] // Vowels Latin cannot place are dropped or written as و.
    [InlineData("kitab", "كيتب")]
    public void Latin_becomes_arabic(string typed, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(typed, Script.Arabic));
    }

    [Fact]
    public void The_digits_people_type_for_arabic_letters_are_read_as_letters()
    {
        Assert.Equal("رح", Transliterator.Convert("r7", Script.Arabic));
        Assert.Equal("رع", Transliterator.Convert("r3", Script.Arabic));

        // A digit is part of the word, so the a after it is not a word-opening alef.
        Assert.Equal("مرحبا", Transliterator.Convert("mar7aba", Script.Arabic));
    }

    [Theory]
    [InlineData("שלום", "shlom")]
    [InlineData("תודה", "toda")]
    [InlineData("צה\"ל", "tsa\"l")]
    public void Hebrew_becomes_latin(string text, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(text));
    }

    [Theory]
    [InlineData("مرحبا", "mrhba")]
    [InlineData("كتاب", "ktab")]
    [InlineData("شكرا", "shkra")]
    public void Arabic_becomes_latin(string text, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(text));
    }

    [Fact]
    public void Text_of_neither_script_is_left_alone()
    {
        Assert.Equal("12:30", Transliterator.Convert("12:30", Script.Hebrew));
        Assert.Equal("!?", Transliterator.Convert("!?", Script.Arabic));
    }
}
