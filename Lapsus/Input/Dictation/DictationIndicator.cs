using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Lapsus.Views;
using System;
using System.Threading;

namespace Lapsus.Input.Dictation;

// The microphone badge, where the layout badge sits: next to the caret, or next to the mouse pointer in apps
// that do not report a caret. The layout badge steps aside while it shows.
internal sealed class DictationIndicator : IDictationIndicator, IDisposable
{
    private static readonly TimeSpan MessageTime = TimeSpan.FromSeconds(4);

    private const double MaxBadgeDip = 480;

    private readonly LayoutIndicator? _layoutIndicator;
    private readonly DispatcherTimer _hideMessage;
    private CaretIndicatorWindow? _window;
    private int _levelPosted;
    private float _level;
    private bool _listening;

    public DictationIndicator(LayoutIndicator? layoutIndicator)
    {
        _layoutIndicator = layoutIndicator;
        _hideMessage = new DispatcherTimer { Interval = MessageTime };
        _hideMessage.Tick += (_, _) => Hide();
    }

    public void ShowListening()
    {
        _listening = true;
        Show(microphone: true, "");
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
            if (_listening && _window is { IsVisible: true } window)
                window.ShowLevel(Volatile.Read(ref _level));
        }, DispatcherPriority.Background);
    }

    public void ShowWorking()
    {
        _listening = false;
        Show(microphone: false, "…");
    }

    public void ShowMessage(string message)
    {
        _listening = false;
        Show(microphone: null, message);
        _hideMessage.Start();
    }

    public void Hide()
    {
        _listening = false;
        _hideMessage.Stop();
        if (_window is { IsVisible: true })
            _window.Hide();
        if (_layoutIndicator is not null)
            _layoutIndicator.Suspended = false;
    }

    private void Show(bool? microphone, string text)
    {
        _hideMessage.Stop();
        if (_layoutIndicator is not null)
            _layoutIndicator.Suspended = true;

        var window = _window ??= new CaretIndicatorWindow();
        var appearing = !window.IsVisible;
        window.Microphone = microphone;
        window.Text = text;

        if (appearing)
        {
            _anchor = Anchor();
            window.Show();
            if (OperatingSystem.IsMacOS())
                FindMacCaretLater(++_placement);
        }

        Place(window, _anchor);
    }

    private CaretBounds _anchor;
    private int _placement;

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

    // The caret if the app reports one (Windows reads it at once), else a pointer-sized box at the mouse.
    private static CaretBounds Anchor()
    {
        if (OperatingSystem.IsWindows())
        {
            var caret = WindowsCaretProbe.Read();
            if (!caret.IsEmpty)
                return caret;
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
        _window?.Close();
        _window = null;
    }
}
