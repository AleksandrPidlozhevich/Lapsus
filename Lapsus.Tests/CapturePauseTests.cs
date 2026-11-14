using Lapsus.Input;

namespace Lapsus.Tests;

public sealed class CapturePauseTests
{
    private DateTime _now = new(2026, 11, 14, 12, 0, 0);

    private CapturePause Pause() => new(() => _now);

    [Fact]
    public void A_pause_for_a_while_ends_by_itself()
    {
        var pause = Pause();
        Assert.False(pause.IsPaused);

        pause.For(TimeSpan.FromMinutes(15));
        Assert.True(pause.IsPaused);

        _now = _now.AddMinutes(14);
        Assert.True(pause.IsPaused);

        _now = _now.AddMinutes(1);
        Assert.False(pause.IsPaused);
    }

    [Fact]
    public void An_open_ended_pause_lasts_until_it_is_ended()
    {
        var pause = Pause();
        pause.For(null);

        _now = _now.AddYears(1);
        Assert.True(pause.IsPaused);

        pause.Resume();
        Assert.False(pause.IsPaused);
    }

    [Fact]
    public void A_game_pauses_on_its_own_and_ends_nothing_the_user_asked_for()
    {
        var pause = Pause();

        pause.GameRunning = true;
        Assert.True(pause.IsPaused);
        Assert.False(pause.ByHand);

        pause.For(TimeSpan.FromHours(1));
        pause.GameRunning = false;
        Assert.True(pause.IsPaused);
    }
}
