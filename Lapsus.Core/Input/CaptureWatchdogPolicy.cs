namespace Lapsus.Core.Input;

public enum WatchdogAction
{
    Nothing,
    StopForSleep,
    StopForRevoke,
    Restart
}

// What the capture watchdog should do on one timer tick. Pure: the platform backend reports what it
// observes and carries out the answer, so every branch can be tested without macOS.
public static class CaptureWatchdogPolicy
{
    public const int PeriodMs = 2000;

    // A tick arriving more than this many periods late means the machine slept or the session was locked.
    public const int SleepGapFactor = 3;

    public static WatchdogAction Decide(in WatchdogObservation observed)
    {
        if (!observed.WantCapture)
            return WatchdogAction.Nothing;

        if (observed.Running)
        {
            if (observed.GapMs > PeriodMs * SleepGapFactor)
                return WatchdogAction.StopForSleep;

            return observed.PermissionsGranted ? WatchdogAction.Nothing : WatchdogAction.StopForRevoke;
        }

        if (!observed.PermissionsGranted
            || observed.NowMs < observed.RestartNotBeforeMs
            || observed.RestartInFlight)
            return WatchdogAction.Nothing;

        return WatchdogAction.Restart;
    }
}

public readonly record struct WatchdogObservation(
    bool WantCapture,
    bool Running,
    bool PermissionsGranted,
    long GapMs,
    long NowMs,
    long RestartNotBeforeMs,
    bool RestartInFlight);
