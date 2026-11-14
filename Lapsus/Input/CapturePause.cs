using System;

namespace Lapsus.Input;

// Why capture is off for now though the user left it switched on: they asked for a break from the tray, or a
// full-screen game is running. Nothing here is saved — a restart always comes back unpaused.
internal sealed class CapturePause(Func<DateTime> now)
{
    private DateTime? _until;

    // null: until resumed by hand.
    public void For(TimeSpan? duration)
    {
        _until = duration is { } time ? now() + time : DateTime.MaxValue;
    }

    public void Resume()
    {
        _until = null;
    }

    public bool GameRunning { get; set; }

    // Asked for from the tray, and the time has not run out.
    public bool ByHand => _until is { } until && now() < until;

    public bool IsPaused => ByHand || GameRunning;
}
