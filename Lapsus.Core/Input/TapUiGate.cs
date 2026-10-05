namespace Lapsus.Core.Input;

// Whether work posted from the macOS event-tap thread may still touch UI-owned state, and whether a
// hotkey may be swallowed before that state is known. Windows runs its hook on the UI thread and does
// not use this.
public static class TapUiGate
{
    // A posted tap job is stale once Stop has moved the epoch, or once capture is off.
    public static bool Accept(int postedEpoch, int currentEpoch, bool capturing)
        => capturing && postedEpoch == currentEpoch;

    // Fail open: swallow only when this pid was already classified on the UI thread and is not excluded.
    // An unknown pid is delivered to the app so the tap never blocks on Accessibility to find out.
    public static bool SwallowHotkey(int eventPid, int snapshotPid, bool snapshotExcluded)
        => eventPid > 0 && eventPid == snapshotPid && !snapshotExcluded;
}
