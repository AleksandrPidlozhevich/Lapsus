using Avalonia.Threading;
using Lapsus.Core.Input;
using System;

namespace Lapsus.Input;

// Records the next key combination for a hotkey field in Settings. Fed from the key hook's thread.
internal sealed class HotkeyRecorder
{
    private Action<HotkeyCaptureEvent>? _sink;

    // Read on the key hook's thread: a capture is on while this is true.
    public bool IsActive => _sink is not null;

    public void Begin(Action<HotkeyCaptureEvent> sink)
    {
        _sink = sink;
    }

    public void Cancel()
    {
        _sink = null;
    }

    // True means the event belongs to the capture and must not reach the app. The sink always runs on the UI thread.
    public bool Feed(int key, bool isDown, HotkeyKeyKind kind, HotkeyModifiers held)
    {
        var sink = _sink;
        if (sink is null)
            return false;

        if (kind == HotkeyKeyKind.Modifier)
        {
            Post(sink, HotkeyCaptureKind.Held, held, 0);
            return false;
        }

        if (!isDown)
            return false;

        if (kind == HotkeyKeyKind.Escape)
        {
            _sink = null;
            Post(sink, HotkeyCaptureKind.Cancelled, held, 0);
            return true;
        }

        // Shift alone only types a capital letter, so a typing key needs a modifier other than Shift.
        var needsShortcut = kind == HotkeyKeyKind.Typing && (held & ~HotkeyModifiers.Shift) == 0;
        if (needsShortcut)
        {
            Post(sink, HotkeyCaptureKind.Rejected, held, 0);
            return true;
        }

        _sink = null;
        Post(sink, HotkeyCaptureKind.Captured, held, HotkeyCombo.Encode(key, held));
        return true;
    }

    private static void Post(Action<HotkeyCaptureEvent> sink, HotkeyCaptureKind kind, HotkeyModifiers held, int trigger)
    {
        var evt = new HotkeyCaptureEvent(kind, held, trigger);
        Dispatcher.UIThread.Post(() => sink(evt));
    }
}
