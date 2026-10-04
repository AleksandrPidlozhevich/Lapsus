using Lapsus.Core.Input;

namespace Lapsus.Core.Tests;

public class CaptureWatchdogPolicyTests
{
    private const long Period = CaptureWatchdogPolicy.PeriodMs;

    private static WatchdogObservation Observe(
        bool want = true, bool running = false, bool granted = true, long gap = Period,
        long now = 100_000, long notBefore = 0, bool inFlight = false) =>
        new(want, running, granted, gap, now, notBefore, inFlight);

    [Fact]
    public void Nothing_happens_when_the_user_does_not_want_capture()
    {
        Assert.Equal(WatchdogAction.Nothing,
            CaptureWatchdogPolicy.Decide(Observe(want: false, running: true, granted: false, gap: 999_999)));
    }

    [Fact]
    public void A_running_capture_with_permissions_and_no_gap_is_left_alone()
    {
        Assert.Equal(WatchdogAction.Nothing, CaptureWatchdogPolicy.Decide(Observe(running: true)));
    }

    [Fact]
    public void A_running_capture_stops_when_a_permission_is_revoked()
    {
        Assert.Equal(WatchdogAction.StopForRevoke,
            CaptureWatchdogPolicy.Decide(Observe(running: true, granted: false)));
    }

    [Fact]
    public void A_long_gap_while_running_means_sleep_and_stops_for_a_restart()
    {
        Assert.Equal(WatchdogAction.StopForSleep,
            CaptureWatchdogPolicy.Decide(Observe(running: true, gap: Period * 3 + 1)));
    }

    [Fact]
    public void A_gap_of_exactly_three_periods_is_not_treated_as_sleep()
    {
        Assert.Equal(WatchdogAction.Nothing,
            CaptureWatchdogPolicy.Decide(Observe(running: true, gap: Period * 3)));
    }

    [Fact]
    public void A_stopped_capture_restarts_once_permissions_are_granted()
    {
        Assert.Equal(WatchdogAction.Restart, CaptureWatchdogPolicy.Decide(Observe()));
    }

    [Fact]
    public void A_stopped_capture_waits_while_permissions_are_still_missing()
    {
        Assert.Equal(WatchdogAction.Nothing, CaptureWatchdogPolicy.Decide(Observe(granted: false)));
    }

    [Fact]
    public void A_stopped_capture_respects_the_restart_backoff()
    {
        Assert.Equal(WatchdogAction.Nothing,
            CaptureWatchdogPolicy.Decide(Observe(now: 1_000, notBefore: 1_001)));
    }

    [Fact]
    public void A_restart_already_in_flight_is_not_repeated()
    {
        Assert.Equal(WatchdogAction.Nothing, CaptureWatchdogPolicy.Decide(Observe(inFlight: true)));
    }
}
