using System;

namespace Lapsus.Input;

// Where an app reports no caret (a browser, an Electron app) the layout badge has no place in the text, and
// one that followed the mouse all day would be in the way. So it shows by the pointer only for a moment:
// when the layout changes, or when it is asked to.
internal struct PointerBadgeClock
{
    public const int ShowMilliseconds = 1200;

    private string? _layout;
    private long _until;

    // Called on every sample, with or without a caret, so a change is seen whichever app it happened in.
    public void Note(string? layout, long now)
    {
        if (layout is not null && _layout is not null && layout != _layout)
            _until = now + ShowMilliseconds;

        _layout = layout ?? _layout;
    }

    public void ShowFor(int milliseconds, long now)
    {
        _until = Math.Max(_until, now + milliseconds);
    }

    public readonly bool IsShowing(long now)
    {
        return now < _until;
    }
}
