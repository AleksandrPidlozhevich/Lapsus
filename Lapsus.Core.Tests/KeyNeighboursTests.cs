using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests;

public class KeyNeighboursTests
{
    [Fact]
    public void A_key_touches_two_keys_on_each_side_row_and_one_on_its_own()
    {
        Assert.Equal(["r", "t", "d", "g", "c", "v"],
            KeyNeighbours.Of('f', BundledKeyboardMaps.En).Select(c => c.ToString()).ToArray());
    }

    [Fact]
    public void Neighbours_follow_the_key_not_what_it_prints()
    {
        // а sits where f does on ЙЦУКЕН.
        Assert.Equal(["к", "е", "в", "п", "с", "м"],
            KeyNeighbours.Of('а', BundledKeyboardMaps.Ru).Select(c => c.ToString()).ToArray());
        Assert.True(KeyNeighbours.AreNeighbours('а', 'п', BundledKeyboardMaps.Ru));
        Assert.False(KeyNeighbours.AreNeighbours('а', 'о', BundledKeyboardMaps.Ru));
    }

    [Fact]
    public void Case_is_kept()
    {
        Assert.Contains('R', KeyNeighbours.Of('F', BundledKeyboardMaps.En));
    }
}
