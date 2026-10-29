using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Lapsus.Audio;
using Lapsus.Views;
using System;
using System.Threading;

namespace Lapsus.Input.Dictation;

// The microphone badge, where the layout badge sits: next to the caret, or next to the mouse pointer in apps
// that do not report a caret. The layout badge steps aside while it shows.
// Also the start and stop tones, and the on-screen microphone button, which mirrors the badge's state.
internal sealed class DictationIndicator : IDictationIndicator, IDisposable
{
    private static readonly TimeSpan MessageTime = TimeSpan.FromSeconds(4);

    // How long the badge says how to finish before it shrinks to the microphone alone.
    private static readonly TimeSpan HintTime = TimeSpan.FromSeconds(3);

    private const double MaxBadgeDip = 480;

    private readonly LayoutIndicator? _layoutIndicator;
    private readonly Func<string> _listeningHint;
    private readonly Func<bool> _sounds;
    private readonly DispatcherTimer _hideMessage;
    private readonly DispatcherTimer _hideHint;
    private CaretIndicatorWindow? _window;
    private DictationButtonWindow? _button;
    private int _levelPosted;
    private float _level;
    private bool _listening;

    public DictationIndicator(LayoutIndicator? layoutIndicator, Func<string> listeningHint, Func<bool> sounds)
    {
        _layoutIndicator = layoutIndicator;
        _listeningHint = listeningHint;
        _sounds = sounds;
        _hideMessage = new DispatcherTimer { Interval = MessageTime };
        _hideMessage.Tick += (_, _) => Hide();
        _hideHint = new DispatcherTimer { Interval = HintTime };
        _hideHint.Tick += (_, _) =>
        {
            _hideHint.Stop();
            if (_listening)
                Show(microphone: true, "");
        };
    }

    // The button's click; set once the session exists.
    public Action? ButtonClicked { get; set; }

    public void SetButton(bool visible, string hotkey, string tip)
    {
        if (!visible)
        {
            _button?.Hide();
            return;
        }

        if (_button is null)
        {
            _button = new DictationButtonWindow();
            _button.Clicked += () => ButtonClicked?.Invoke();
        }

        _button.Describe(hotkey, tip);
        if (!_button.IsVisible)
            _button.Show();
    }

    public void ShowListening()
    {
        _listening = true;
        if (_sounds())
            DictationSounds.Play(start: true);
        Show(microphone: true, _listeningHint());
        _hideHint.Start();
    }

    public void SetLevel(float level)
    {
        Volatile.Write(ref _level, level);
        // At most one repaint queued: the capture thread reports twenty blocks a second.
        if (Interlocked.Exchange(ref _levelPosted, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _levelPosted, 0);
            if (!_listening)
                return;
            var level = Volatile.Read(ref _level);
            if (_window is { IsVisible: true } window)
                window.ShowLevel(level);
            if (_button is { IsVisible: true } button)
                button.ShowLevel(level);
        }, DispatcherPriority.Background);
    }

    public void ShowWorking()
    {
        _listening = false;
        _hideHint.Stop();
        if (_sounds())
            DictationSounds.Play(start: false);
        Show(microphone: false, "…");
    }

    public void ShowMessage(string message)
    {
        _listening = false;
        _hideHint.Stop();
        Show(microphone: null, message);
        _hideMessage.Start();
    }

    public void Hide()
    {
        _listening = false;
        _onButton = false;
        _hideHint.Stop();
        _hideMessage.Stop();
        _button?.ShowState(null, null);
        if (_window is { IsVisible: true })
            _window.Hide();
        if (_layoutIndicator is not null)
            _layoutIndicator.Suspended = false;
    }

    // One badge per dictation: at the caret when the field reports one, otherwise the on-screen button says it
    // all (when it is shown), and only without either does a badge follow the mouse pointer.
    private void Show(bool? microphone, string text)
    {
        _hideMessage.Stop();
        if (_layoutIndicator is not null)
            _layoutIndicator.Suspended = true;

        var window = _window ??= new CaretIndicatorWindow();
        if (!_onButton && !window.IsVisible)
        {
            if (Anchor() is { } anchor)
            {
                _anchor = anchor;
                window.Show();
                if (OperatingSystem.IsMacOS())
                    FindMacCaretLater(++_placement);
            }
            else
            {
                _onButton = true;
            }
        }

        if (_onButton)
        {
            _button?.ShowState(microphone, text);
            return;
        }

        window.Microphone = microphone;
        window.Text = text;
        Place(window, _anchor);
        _button?.ShowState(microphone, null);
    }

    private CaretBounds _anchor;
    private int _placement;
    private bool _onButton;

    private static void Place(CaretIndicatorWindow window, CaretBounds anchor)
    {
        var screen = window.Screens?.ScreenFromPoint(new PixelPoint(anchor.Left, anchor.Top)) ?? window.Screens?.Primary;
        var scale = screen?.Scaling ?? 1;
        var size = window.Bounds.Size;
        var badge = size.Width > 0 && size.Height > 0
            ? PixelSize.FromSize(new Size(Math.Min(size.Width, MaxBadgeDip), Math.Min(size.Height, MaxBadgeDip)), scale)
            : default;
        window.MoveTo(CaretBadgePlacement.For(anchor, badge, screen?.WorkingArea ?? default, scale));
    }

    // The caret if the app reports one (Windows reads it at once); null when there is none and the button is up;
    // else a pointer-sized box at the mouse.
    private CaretBounds? Anchor()
    {
        if (OperatingSystem.IsWindows())
        {
            var caret = WindowsCaretProbe.Read();
            if (!caret.IsEmpty)
                return caret;
            if (_button is { IsVisible: true })
                return null;
            if (NativeMethods.GetCursorPos(out var point))
                return AtPointer(point.x, point.y);
        }
        else if (OperatingSystem.IsMacOS() && MacPointer.Location() is { } pointer)
        {
            return AtPointer(pointer.X, pointer.Y);
        }

        return new CaretBounds(0, 0, 1, 18);
    }

    private static CaretBounds AtPointer(int x, int y) => new(x, y, x + 1, y + 18);

    // Accessibility can stall for its whole timeout on a busy app, so on macOS the badge appears at the
    // pointer and moves to the caret once a worker has found it. The sandbox has no caret to find.
    private void FindMacCaretLater(int placement)
    {
        if (!OperatingSystem.IsMacOS() || !LayoutIndicator.IsSupported)
            return;

        _ = System.Threading.Tasks.Task.Run(MacCaretProbe.Read).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (placement != _placement || !t.IsCompletedSuccessfully || t.Result.IsEmpty
                || _window is not { IsVisible: true } window)
                return;
            _anchor = t.Result;
            Place(window, _anchor);
        }), System.Threading.Tasks.TaskScheduler.Default);
    }

    public void Dispose()
    {
        _hideMessage.Stop();
        _hideHint.Stop();
        _window?.Close();
        _window = null;
        _button?.Close();
        _button = null;
    }
}
