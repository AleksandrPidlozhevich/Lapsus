namespace Lapsus.Input;

// CGEventCreateKeyboardEvent leaves the timestamp at 0. Chromium drops any key older than the last
// real one, so a burst of deletes and the paste chord never reaches Electron.
internal static class InjectedEventTime
{
    internal const ulong StepNanoseconds = 1_000_000;

    internal static ulong Next(ulong now, ref ulong last)
    {
        var stamp = last + StepNanoseconds;
        if (stamp < now)
            stamp = now;

        last = stamp;
        return stamp;
    }
}
