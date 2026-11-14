using Lapsus.Input;

namespace Lapsus.Tests;

public class PointerBadgeClockTests
{
    [Fact]
    public void The_badge_shows_for_a_moment_when_the_layout_changes()
    {
        var clock = new PointerBadgeClock();

        clock.Note("EN", 0);
        Assert.False(clock.IsShowing(0));

        clock.Note("EN", 100);
        Assert.False(clock.IsShowing(100));

        clock.Note("UK", 200);
        Assert.True(clock.IsShowing(200));
        Assert.True(clock.IsShowing(200 + PointerBadgeClock.ShowMilliseconds - 1));
        Assert.False(clock.IsShowing(200 + PointerBadgeClock.ShowMilliseconds));
    }

    [Fact]
    public void A_sample_that_could_not_read_the_layout_is_not_a_change()
    {
        var clock = new PointerBadgeClock();

        clock.Note("EN", 0);
        clock.Note(null, 100);
        clock.Note("EN", 200);

        Assert.False(clock.IsShowing(200));
    }

    [Fact]
    public void A_mark_asked_for_shows_for_as_long_as_asked_and_never_cuts_a_longer_one_short()
    {
        var clock = new PointerBadgeClock();
        clock.Note("EN", 0);
        clock.Note("UK", 0);

        clock.ShowFor(700, 100);

        Assert.True(clock.IsShowing(PointerBadgeClock.ShowMilliseconds - 1));
    }
}
