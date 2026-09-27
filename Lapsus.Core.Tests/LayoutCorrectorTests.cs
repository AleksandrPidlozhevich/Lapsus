using Lapsus.Core.Correction;

namespace Lapsus.Core.Tests;

public class LayoutCorrectorTests
{
    private readonly LayoutCorrector _corrector = new(null);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Auto_handles_empty_input(string input)
    {
        var result = _corrector.CorrectPhrase(input);
        Assert.False(result.Changed);
        Assert.Equal(input, result.Corrected);
    }

    [Theory]
    [InlineData("-“cat")]
    [InlineData("-“привет")]
    [InlineData("=/«word")]
    public void Untypable_punctuation_does_not_drop_the_characters_before_it(string input)
    {
        var result = _corrector.CorrectPhrase(input);
        Assert.Equal(input, result.Corrected);
    }
}
