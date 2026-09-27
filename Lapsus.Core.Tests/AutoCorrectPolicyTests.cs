using Lapsus.Core.Correction;

namespace Lapsus.Core.Tests;

public class AutoCorrectPolicyTests
{
    [Theory]
    [InlineData("d")]
    [InlineData("yt")]
    [InlineData("z,")]
    public void A_short_chunk_is_eligible_only_once_the_line_has_a_direction(string word)
    {
        Assert.False(AutoCorrectPolicy.IsEligible(word));
        Assert.True(AutoCorrectPolicy.IsEligible(word, lineHasDirection: true));
    }

    [Theory]
    [InlineData("D")]
    [InlineData("1")]
    [InlineData("!")]
    public void A_line_direction_relaxes_the_length_gate_and_nothing_else(string word)
    {
        Assert.False(AutoCorrectPolicy.IsEligible(word, lineHasDirection: true));
    }

    [Theory]
    [InlineData(";")]
    [InlineData("'")]
    public void A_key_that_is_a_letter_elsewhere_is_eligible_once_the_line_has_a_direction(string word)
    {
        Assert.False(AutoCorrectPolicy.IsEligible(word));
        Assert.True(AutoCorrectPolicy.IsEligible(word, lineHasDirection: true));
    }

    [Theory]
    [InlineData("dsnf.")]
    [InlineData("Dsnf.")]
    [InlineData("вітаю")]
    [InlineData("გამარჯობა")]
    public void Eligible_plain_words(string word)
    {
        Assert.True(AutoCorrectPolicy.IsEligible(word));
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("a")]
    [InlineData("DSNF>")]
    [InlineData("iPhone")]
    [InlineData("myVar")]
    [InlineData("abc123")]
    [InlineData("v2")]
    public void Skips_special_tokens(string word)
    {
        Assert.False(AutoCorrectPolicy.IsEligible(word));
    }

    [Theory]
    [InlineData("qarTuli")] // Shift+T is თ
    [InlineData("Zalian")] // Shift+Z is ძ
    [InlineData("bavSvi")] // Shift+S is შ
    [InlineData("haJi")] // Shift+J is a letter on Arabic and Georgian
    public void Eligible_when_the_internal_capital_is_a_shifted_level_letter(string word)
    {
        Assert.True(AutoCorrectPolicy.IsEligible(word));
    }

    [Theory]
    [InlineData("hf,jnf")]
    [InlineData("[jhjij")]
    [InlineData(";bpym")]
    [InlineData("j,]tv")]
    [InlineData(",usv")]
    public void Eligible_when_the_punctuation_is_a_letter_key(string word)
    {
        Assert.True(AutoCorrectPolicy.IsEligible(word));
    }

    [Theory]
    [InlineData("co-op")]
    [InlineData("a+b+c")]
    [InlineData("x=y=z")]
    [InlineData("one_two")]
    public void Still_skips_punctuation_that_is_nobody_s_letter(string word)
    {
        Assert.False(AutoCorrectPolicy.IsEligible(word));
    }

    [Theory]
    [InlineData("myVar")]
    [InlineData("iPhone")]
    [InlineData("someKey")]
    [InlineData("aBc")]
    public void Still_skips_camel_case_on_keys_without_a_shifted_letter(string word)
    {
        Assert.False(AutoCorrectPolicy.IsEligible(word));
    }

    [Theory]
    [InlineData("JavaScript")]
    [InlineData("TypeScript")]
    [InlineData("YouTube")]
    [InlineData("iTunes")]
    public void Camel_case_built_from_shifted_level_keys_is_the_known_cost(string word)
    {
        Assert.True(AutoCorrectPolicy.IsEligible(word));
    }

    [Fact]
    public void The_excused_capitals_are_exactly_the_shifted_level_letter_keys()
    {
        var excused = new List<char>();
        for (var c = 'A'; c <= 'Z'; c++)
            if (AutoCorrectPolicy.IsEligible($"a{c}bc"))
                excused.Add(c);

        Assert.Equal("CHJNRSTWYZ", new string(excused.ToArray()));
    }
}
