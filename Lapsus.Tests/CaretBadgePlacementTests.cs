using Avalonia;
using Lapsus.Input;

namespace Lapsus.Tests;

public class CaretBadgePlacementTests
{
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1040);

    private static readonly PixelSize Badge = new(28, 18);

    private static CaretBounds CaretAt(int x, int y) => new(x, y, x + 2, y + 17);

    [Fact]
    public void Sits_right_of_the_caret_and_below_its_line()
    {
        var position = CaretBadgePlacement.For(CaretAt(500, 400), Badge, WorkArea, 1);

        Assert.Equal(new PixelPoint(510, 419), position);
    }

    [Fact]
    public void The_gaps_are_dips_so_they_survive_a_scaled_display()
    {
        var caret = CaretAt(500, 400);

        var unscaled = CaretBadgePlacement.For(caret, Badge, WorkArea, 1);
        var scaled = CaretBadgePlacement.For(caret, Badge, WorkArea, 2);

        Assert.Equal(8, unscaled.X - caret.Right);
        Assert.Equal(16, scaled.X - caret.Right);
        Assert.Equal(4, scaled.Y - caret.Bottom);
    }

    [Fact]
    public void Near_the_right_edge_it_moves_to_the_other_side_of_the_caret()
    {
        var caret = CaretAt(1910, 400);

        var position = CaretBadgePlacement.For(caret, Badge, WorkArea, 1);

        Assert.Equal(caret.Left - 8 - Badge.Width, position.X);
        Assert.True(position.X + Badge.Width <= WorkArea.Right);
    }

    [Fact]
    public void On_the_last_line_of_the_screen_it_goes_above_the_caret()
    {
        var caret = CaretAt(500, 1030);

        var position = CaretBadgePlacement.For(caret, Badge, WorkArea, 1);

        Assert.Equal(caret.Top - 2 - Badge.Height, position.Y);
        Assert.True(position.Y + Badge.Height <= WorkArea.Bottom);
    }

    [Fact]
    public void A_caret_in_the_far_corner_flips_on_both_axes()
    {
        var position = CaretBadgePlacement.For(CaretAt(1915, 1035), Badge, WorkArea, 1);

        Assert.True(position.X + Badge.Width <= WorkArea.Right);
        Assert.True(position.Y + Badge.Height <= WorkArea.Bottom);
    }

    [Fact]
    public void A_caret_outside_the_work_area_still_leaves_the_badge_on_screen()
    {
        var position = CaretBadgePlacement.For(CaretAt(-300, -300), Badge, WorkArea, 1);

        Assert.True(WorkArea.Contains(new PixelRect(position, Badge)));
    }

    [Fact]
    public void A_second_monitor_is_placed_in_its_own_coordinates()
    {
        var right = new PixelRect(1920, 0, 1920, 1040);

        var position = CaretBadgePlacement.For(CaretAt(3830, 400), Badge, right, 1);

        Assert.True(right.Contains(new PixelRect(position, Badge)));
        Assert.True(position.X >= right.X);
    }

    [Fact]
    public void An_unmeasured_badge_is_anchored_rather_than_clamped_against_a_guess()
    {
        var position = CaretBadgePlacement.For(CaretAt(1910, 1035), default, WorkArea, 1);

        Assert.Equal(new PixelPoint(1920, 1054), position);
    }

    [Fact]
    public void With_no_screen_identified_nothing_is_clamped()
    {
        var position = CaretBadgePlacement.For(CaretAt(500, 400), Badge, default, 1);

        Assert.Equal(new PixelPoint(510, 419), position);
    }

    [Fact]
    public void A_badge_larger_than_the_screen_lands_on_it_anyway()
    {
        var tiny = new PixelRect(0, 0, 20, 10);

        var position = CaretBadgePlacement.For(CaretAt(5, 5), Badge, tiny, 1);

        Assert.Equal(new PixelPoint(0, 0), position);
    }
}
