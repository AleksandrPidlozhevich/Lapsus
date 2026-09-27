using Lapsus.Input;

namespace Lapsus.Tests;

public class CaretScreenMappingTests
{
    private static CaretBounds Sliver(int x, int y, int width = 2, int height = 17)
        => new(x, y, x + width, y + height);

    [Fact]
    public void A_client_caret_is_offset_by_the_window_origin()
    {
        var screen = CaretScreenMapping.MapToScreen(Sliver(10, 20), originX: 100, originY: 200, 800, 600);

        Assert.Equal(new CaretBounds(110, 220, 112, 237), screen);
    }

    [Fact]
    public void A_caret_outside_the_owner_is_dropped()
    {
        var screen = CaretScreenMapping.MapToScreen(Sliver(10, 500), originX: 0, originY: 0, 800, 400);

        Assert.True(screen.IsEmpty);
    }

    [Fact]
    public void A_window_tall_rect_is_not_a_caret()
    {
        var screen = CaretScreenMapping.MapToScreen(new CaretBounds(0, 0, 800, 600), 0, 0, 800, 600);

        Assert.True(screen.IsEmpty);
    }

    [Fact]
    public void A_zero_width_sliver_is_still_a_caret()
    {
        Assert.True(CaretScreenMapping.IsPlausibleSliver(new CaretBounds(10, 20, 10, 37)));
    }

    [Fact]
    public void The_ime_default_is_the_bottom_left_of_a_tall_window()
    {
        Assert.True(CaretScreenMapping.IsDefaultImeAnchor(Sliver(0, 580), 800, 600));
    }

    [Fact]
    public void A_caret_at_the_start_of_a_document_is_not_the_ime_default()
    {
        Assert.False(CaretScreenMapping.IsDefaultImeAnchor(Sliver(0, 0), 800, 600));
    }

    [Fact]
    public void A_short_edit_at_the_bottom_of_the_parent_is_not_the_ime_default()
    {
        Assert.False(CaretScreenMapping.IsDefaultImeAnchor(Sliver(8, 20), 400, 40));
    }

    [Fact]
    public void An_unaware_caret_is_scaled_before_the_origin_is_added()
    {
        var physical = CaretScreenMapping.Scale(Sliver(10, 20), 1.5);

        Assert.Equal(new CaretBounds(15, 30, 18, 56), physical);
    }

    [Fact]
    public void Moving_the_owner_moves_the_mapped_caret()
    {
        var caret = Sliver(10, 20);

        var before = CaretScreenMapping.MapToScreen(caret, originX: 100, originY: 200, 800, 600);
        var after = CaretScreenMapping.MapToScreen(caret, originX: 400, originY: 80, 800, 600);

        Assert.Equal(300, after.Left - before.Left);
        Assert.Equal(-120, after.Top - before.Top);
    }
}
