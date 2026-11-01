using Avalonia.Threading;
using Lapsus.Core.Input;
using System;

namespace Lapsus.Input;

// A dictation hotkey of modifiers alone (Ctrl+Win, ⌃⌥): hold to talk, a quick tap toggles. UI thread only.
internal sealed class DictationChordDetector(Func<int> hotkey, Action pressed, Action<bool> released)
{
    // Held this long before the microphone opens, so Ctrl+Win+→ and the like never flash it.
    private static readonly TimeSpan ChordDelay = TimeSpan.FromMilliseconds(150);

    private HotkeyModifiers _lastHeld;
    private bool _chordHeld;
    private bool _chordSpoiled;
    private bool _chordPressed;
    private int _chordGeneration;

    private HotkeyModifiers Chord =>
        HotkeyCombo.IsCombo(hotkey()) && HotkeyCombo.KeyOf(hotkey()) == 0
            ? HotkeyCombo.ModifiersOf(hotkey())
            : HotkeyModifiers.None;

    // Every modifier going down or up, with the whole set held now. True when this completed the chord.
    public bool NoteModifiers(HotkeyModifiers held)
    {
        var previous = _lastHeld;
        _lastHeld = held;
        var chord = Chord;
        if (chord == HotkeyModifiers.None || held == previous)
            return false;

        if (held == chord)
        {
            // Reached by letting go of a key (Shift out of Ctrl+Win+Shift) it is the tail of another shortcut.
            if ((previous & ~chord) != 0)
                return false;

            _chordHeld = true;
            _chordSpoiled = false;
            _chordPressed = false;
            var generation = ++_chordGeneration;
            DispatcherTimer.RunOnce(() =>
            {
                if (generation != _chordGeneration || !_chordHeld || _chordSpoiled)
                    return;
                _chordPressed = true;
                pressed();
            }, ChordDelay);
            return true;
        }

        if (!_chordHeld)
            return false;

        _chordHeld = false;
        _chordGeneration++;
        var letGo = (held & ~chord) == 0;
        if (_chordPressed)
        {
            released(!letGo);
        }
        else if (letGo && !_chordSpoiled)
        {
            pressed();
            released(false);
        }

        _chordPressed = false;
        return false;
    }

    // While the chord is held, another key makes it a different shortcut.
    public void NoteOtherKey()
    {
        if (!_chordHeld || _chordSpoiled)
            return;

        _chordSpoiled = true;
        if (!_chordPressed)
            return;

        _chordPressed = false;
        released(true);
    }
}
