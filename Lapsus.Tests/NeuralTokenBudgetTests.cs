using Lapsus.Neural;

namespace Lapsus.Tests;

public class NeuralTokenBudgetTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(40)]
    [InlineData(100)]
    public void A_fragment_gets_more_tokens_than_it_has_characters(int chars)
    {
        Assert.True(OnnxTextGenerator.TokenBudget(chars) > chars);
    }

    [Fact]
    public void A_longer_fragment_gets_a_longer_budget()
    {
        Assert.True(OnnxTextGenerator.TokenBudget(50) > OnnxTextGenerator.TokenBudget(10));
    }

    [Fact]
    public void A_fragment_too_long_to_answer_is_refused_rather_than_truncated()
    {
        Assert.Equal(0, OnnxTextGenerator.TokenBudget(4000));
    }

    [Fact]
    public void The_ceiling_is_the_only_refusal()
    {
        var ceiling = 0;
        for (var chars = 1; chars <= 4000; chars++)
            if (OnnxTextGenerator.TokenBudget(chars) == 0)
            {
                ceiling = chars;
                break;
            }

        Assert.True(ceiling > 100, $"a line of ordinary length must still reach the model, got {ceiling}");
        for (var chars = 1; chars < ceiling; chars++)
            Assert.True(OnnxTextGenerator.TokenBudget(chars) > 0);
    }
}
