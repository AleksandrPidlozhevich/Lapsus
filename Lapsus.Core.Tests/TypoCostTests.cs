using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests;

public class TypoCostTests
{
    [Fact]
    public void A_dropped_key_is_likelier_than_a_letter_from_across_the_board()
    {
        var dropped = TypoCost.Between("мауть", "мабуть", BundledKeyboardMaps.Uk);
        var far = TypoCost.Between("мауть", "мають", BundledKeyboardMaps.Uk);

        Assert.True(dropped < far, $"dropped {dropped}, far {far}");
    }

    [Fact]
    public void The_key_next_door_is_the_cheapest_slip()
    {
        // g sits beside f; p is across the board.
        Assert.True(TypoCost.Between("gine", "fine", BundledKeyboardMaps.En) <
                    TypoCost.Between("pine", "fine", BundledKeyboardMaps.En));
    }

    [Fact]
    public void Two_letters_swapped_cost_less_than_two_replaced()
    {
        Assert.Equal(TypoCost.Transposition, TypoCost.Between("teh", "the", BundledKeyboardMaps.En));
    }

    [Fact]
    public void The_same_word_costs_nothing_whatever_its_case()
    {
        Assert.Equal(0.0, TypoCost.Between("Hello", "hello", BundledKeyboardMaps.En));
    }
}
