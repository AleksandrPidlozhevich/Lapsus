namespace Lapsus.Core.Input;

// Windows runs its hook on the UI thread and does not use this.
public static class TapUiGate
{
    // A posted tap job is stale once Stop has moved the epoch, or once capture is off.
    public static bool Accept(int postedEpoch, int currentEpoch, bool capturing)
        => capturing && postedEpoch == currentEpoch;

    // Fail open: swallow only when this pid was already classified on the UI thread and is not excluded.
    // An unknown pid is delivered to the app so the tap never blocks on Accessibility to find out.
    public static bool SwallowHotkey(int eventPid, int snapshotPid, bool snapshotExcluded)
        => eventPid > 0 && eventPid == snapshotPid && !snapshotExcluded;

    // A held hotkey repeats its key-down with the autorepeat flag. A fresh press of the same key
    // means its key-up never reached the tap (tap disabled, secure input), so the press is new.
    public static HeldKeyDown ClassifyKeyDown(int? swallowedKey, int key, bool autoRepeat)
    {
        if (swallowedKey != key)
            return HeldKeyDown.Unrelated;

        return autoRepeat ? HeldKeyDown.Repeat : HeldKeyDown.LostKeyUp;
    }
}

public enum HeldKeyDown
{
    Unrelated,
    Repeat,
    LostKeyUp
}
