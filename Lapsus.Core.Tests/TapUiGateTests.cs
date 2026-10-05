using Lapsus.Core.Input;

namespace Lapsus.Core.Tests;

public class TapUiGateTests
{
    [Fact]
    public void Work_is_accepted_only_for_the_current_epoch_while_capture_is_running()
    {
        Assert.True(TapUiGate.Accept(postedEpoch: 3, currentEpoch: 3, capturing: true));
    }

    [Fact]
    public void Work_from_a_previous_capture_session_is_dropped()
    {
        Assert.False(TapUiGate.Accept(postedEpoch: 3, currentEpoch: 4, capturing: true));
    }

    [Fact]
    public void Work_is_dropped_while_capture_is_off_even_if_the_epoch_matches()
    {
        Assert.False(TapUiGate.Accept(postedEpoch: 3, currentEpoch: 3, capturing: false));
    }

    [Theory]
    [InlineData(10, 10, false, true)]
    [InlineData(10, 10, true, false)]
    [InlineData(10, 11, false, false)]
    [InlineData(0, 0, false, false)]
    [InlineData(-1, -1, false, false)]
    public void A_hotkey_is_swallowed_only_when_the_pid_matches_and_the_app_is_not_excluded(
        int eventPid, int snapshotPid, bool excluded, bool swallow)
    {
        Assert.Equal(swallow, TapUiGate.SwallowHotkey(eventPid, snapshotPid, excluded));
    }
}
