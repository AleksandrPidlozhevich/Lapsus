using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Lapsus.Views;
using System;
using System.Globalization;
using System.Threading.Tasks;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Input;

public sealed class LayoutIndicator : IDisposable
{

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(120);

    private static readonly TimeSpan MovingInterval = TimeSpan.FromMilliseconds(16);

    private const double MaxBadgeDip = 80;

    private readonly AppExclusions _excludedApps;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _refreshRetry;
    private readonly WinEventDelegate _winEventProc;

    private CaretIndicatorWindow? _window;

    private WindowsFocusProbe? _focus;

    private IntPtr _moveSizeHook;
    private bool _enabled;
    private bool _hostIsMoving;
    private string? _shownText;
    private bool _macSampleInFlight;

    public LayoutIndicator(AppExclusions excludedApps)
    {
        _excludedApps = excludedApps;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
        _timer.Tick += (_, _) => Tick();
        _refreshRetry = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _refreshRetry.Tick += OnRefreshRetry;

        _winEventProc = OnHostMoved;
    }

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;

            _enabled = value;
            if (value && IsSupported)
            {
                HookMoveSize();
                if (OperatingSystem.IsMacOS())
                    MacDisplayChanges.Subscribe(OnDisplaysChanged);
                _timer.Start();
            }
            else
            {
                UnhookMoveSize();
                if (OperatingSystem.IsMacOS())
                    MacDisplayChanges.Unsubscribe();
                _timer.Stop();
                _refreshRetry.Stop();
                _timer.Interval = Interval;
                _hostIsMoving = false;
                Hide();
            }
        }
    }

    public void Dispose()
    {
        Enabled = false;
        _refreshRetry.Stop();
        _window?.Close();
        _window = null;
    }

    private void HookMoveSize()
    {
        if (!OperatingSystem.IsWindows() || _moveSizeHook != IntPtr.Zero)
            return;

        _moveSizeHook = SetWinEventHook(
            EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void UnhookMoveSize()
    {
        if (!OperatingSystem.IsWindows() || _moveSizeHook == IntPtr.Zero)
            return;

        UnhookWinEvent(_moveSizeHook);
        _moveSizeHook = IntPtr.Zero;
    }

    private void OnHostMoved(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_enabled)
                return;

            _hostIsMoving = eventType == EVENT_SYSTEM_MOVESIZESTART;
            _timer.Interval = _hostIsMoving ? MovingInterval : Interval;
            Tick();
        });
    }

    public void Refresh()
    {
        if (!_enabled)
            return;

        Tick();
        _refreshRetry.Stop();
        _refreshRetry.Start();
    }

    private void OnDisplaysChanged()
    {
        if (!_enabled)
            return;

        _shownText = null;
        Refresh();
    }

    private void OnRefreshRetry(object? sender, EventArgs e)
    {
        _refreshRetry.Stop();
        if (_enabled)
            Tick();
    }

    private void Tick()
    {
        if (!IsSupported)
            return;

        if (OperatingSystem.IsMacOS())
        {
            SampleMacOffThread();
            return;
        }

        Apply(ForegroundProcessName(), ReadCaret());
    }

    // Accessibility calls can block for up to their timeout when the target app is busy, so on macOS
    // they run on a worker and the UI thread only applies the result. One sample is in flight at a time.
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private void SampleMacOffThread()
    {
        if (_macSampleInFlight)
            return;

        _macSampleInFlight = true;
        _ = Task.Run(() => new MacSample(MacCaretProbe.ForegroundProcessName(), MacCaretProbe.Read()))
            .ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                _macSampleInFlight = false;
                if (_enabled && t.IsCompletedSuccessfully)
                    Apply(t.Result.ProcessName, t.Result.Caret);
            }), TaskScheduler.Default);
    }

    private void Apply(string? processName, CaretBounds caret)
    {
        if (_excludedApps.Contains(processName))
        {
            Hide();
            return;
        }

        if (caret.IsEmpty)
        {

            if (_hostIsMoving)
                return;

            Hide();
            return;
        }

        if (LayoutCode() is not { } text)
        {
            Hide();
            return;
        }

        Show(text, caret);
    }

    private readonly record struct MacSample(string? ProcessName, CaretBounds Caret);

    private string? ForegroundProcessName()
    {
        if (OperatingSystem.IsWindows())
        {
            _focus ??= new WindowsFocusProbe();
            return _focus.ForegroundProcessName();
        }

        return OperatingSystem.IsMacOS() ? MacCaretProbe.ForegroundProcessName() : null;
    }

    private static CaretBounds ReadCaret()
    {
        if (OperatingSystem.IsWindows())
            return WindowsCaretProbe.Read();

        return OperatingSystem.IsMacOS() ? MacCaretProbe.Read() : default;
    }

    private static string? LayoutCode()
    {
        if (OperatingSystem.IsWindows())
            return WindowsLayoutCode(ForegroundKeyboardLayout());

        if (OperatingSystem.IsMacOS())
            return MacLayoutCode();

        return null;
    }

    private static string? WindowsLayoutCode(IntPtr hkl)
    {
        if (LayoutLanguageResolver.FromWindowsLangId(hkl) is { } known)
            return known.ToUpperInvariant();

        try
        {
            var name = new CultureInfo((int)(hkl.ToInt64() & 0xFFFF)).TwoLetterISOLanguageName;

            return name is { Length: 2 } and not "iv" ? name.ToUpperInvariant() : null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static string? MacLayoutCode()
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        var code = MacInstalledLayouts.CurrentLanguageCode();
        return code is { Length: 2 } and not "iv" ? code.ToUpperInvariant() : null;
    }

    private void Show(string text, CaretBounds caret)
    {
        var window = _window ??= new CaretIndicatorWindow();
        window.Text = text;

        var screen = ScreenAt(window, caret);
        var scale = screen?.Scaling ?? 1;
        var workArea = screen?.WorkingArea ?? default;
        var position = CaretBadgePlacement.For(caret, PixelSizeOf(window, scale), workArea, scale);

        if (_shownText == text && window.IsVisible && window.NativePosition == position)
            return;

        _shownText = text;
        window.MoveTo(position);

        if (window.IsVisible)
            return;

        window.Show();

        window.MoveTo(CaretBadgePlacement.For(caret, PixelSizeOf(window, scale), workArea, scale));
    }

    private static Screen? ScreenAt(Window window, CaretBounds caret)
    {
        var screens = window.Screens;
        return screens?.ScreenFromPoint(new PixelPoint(caret.Left, caret.Top)) ?? screens?.Primary;
    }

    private static PixelSize PixelSizeOf(Window window, double scale)
    {
        var size = window.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return default;

        return PixelSize.FromSize(
            new Size(Math.Min(size.Width, MaxBadgeDip), Math.Min(size.Height, MaxBadgeDip)),
            scale);
    }

    private void Hide()
    {
        if (_window is { IsVisible: true })
            _window.Hide();

        _shownText = null;
    }
}
