using Lapsus.Input;

namespace Lapsus.Tests;

public class MacTrailingSelectionTests
{
    [Fact]
    public void A_range_that_covers_the_trailing_run_is_safe_to_overwrite()
    {
        Assert.True(MacTrailingSelection.Confirmed(4, 3, 4, 3, 3));
    }

    [Fact]
    public void An_empty_selection_is_not_treated_as_covering_the_word()
    {
        // Writing into a caret inserts, so the characters that should have been replaced stay.
        Assert.False(MacTrailingSelection.Confirmed(7, 3, 7, 3, 0));
        Assert.False(MacTrailingSelection.Confirmed(7, 0, 4, 3, 0));
    }

    [Fact]
    public void A_missing_selected_text_attribute_trusts_the_range()
    {
        Assert.True(MacTrailingSelection.Confirmed(0, 3, 0, 3, null));
    }

    [Theory]
    [InlineData(1, 3, 0, 3)]
    [InlineData(0, 2, 0, 3)]
    public void A_range_that_is_not_the_requested_run_is_rejected(
        int location, int length, int expectedLocation, int expectedLength)
    {
        Assert.False(MacTrailingSelection.Confirmed(
            location, length, expectedLocation, expectedLength, length));
    }
}
