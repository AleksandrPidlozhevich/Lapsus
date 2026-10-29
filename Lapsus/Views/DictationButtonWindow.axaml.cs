using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Views;

// The on-screen microphone at the bottom of the screen. A click starts or stops dictation; the window never
// takes focus, so the text still goes to the field the user was typing in.
public partial class DictationButtonWindow : Window
{
    private static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#8B8D98"));
    private static readonly IBrush Listening = new SolidColorBrush(Color.Parse("#E5484D"));

    private const int BottomGap = 10;

    private readonly Border _pill;
    private readonly Canvas _mic;
    private readonly Path _micHead;
    private readonly Path _micStand;
    private readonly TextBlock _label;
    private string _idleText = "";

    public DictationButtonWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _pill = this.FindControl<Border>("Pill")!;
        _mic = this.FindControl<Canvas>("Mic")!;
        _micHead = this.FindControl<Path>("MicHead")!;
        _micStand = this.FindControl<Path>("MicStand")!;
        _label = this.FindControl<TextBlock>("Label")!;
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.AddWndProcHookCallback(this, WndProc);
        }
        else
        {
            _pill.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                    Clicked?.Invoke();
            };
        }

        SizeChanged += (_, _) => PlaceAtBottom();
    }

    private const uint WmMouseActivate = 0x0021;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const int MaNoActivate = 3;

    // WS_EX_NOACTIVATE alone is not enough: Avalonia focuses its window on a button press, which took focus away
    // from the field being dictated into (the desktop ended up active). The clicks are handled here instead.
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmMouseActivate:
                handled = true;
                return MaNoActivate;
            case WmLButtonDown:
                handled = true;
                return IntPtr.Zero;
            case WmLButtonUp:
                handled = true;
                Clicked?.Invoke();
                return IntPtr.Zero;
            default:
                return IntPtr.Zero;
        }
    }

    public event Action? Clicked;

    // The hotkey shown on the idle button, and the longer explanation on hover.
    public void Describe(string hotkey, string tip)
    {
        _idleText = hotkey;
        ToolTip.SetTip(_pill, tip);
        ShowState(_state, _text);
    }

    private bool? _state;
    private string? _text;

    // null: idle; true: listening; false: working on what was said. Text, when given, replaces the usual label
    // (the hint while listening, a message): the button stands in for the caret badge where there is no caret.
    public void ShowState(bool? listening, string? text)
    {
        _state = listening;
        _text = text;
        var brush = listening == true ? Listening : Idle;
        _micHead.Fill = brush;
        _micStand.Stroke = brush;
        _mic.Opacity = 1;
        _label.Text = text is { Length: > 0 }
            ? text
            : listening switch
            {
                null => _idleText,
                true => "",
                false => "…"
            };
        _label.IsVisible = _label.Text.Length > 0;
    }

    public void ShowLevel(float level)
    {
        _mic.Opacity = 0.45 + 0.55 * Math.Clamp(level * 12, 0, 1);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PlaceAtBottom();

        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
            return;

        // Clickable, but never activated: focus stays in the app being dictated into.
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    // Centred above the taskbar of the main screen.
    private void PlaceAtBottom()
    {
        if (Screens.Primary is not { } screen || Bounds.Width <= 0)
            return;

        var area = screen.WorkingArea;
        var size = PixelSize.FromSize(Bounds.Size, screen.Scaling);
        Position = new PixelPoint(
            area.X + (area.Width - size.Width) / 2,
            area.Bottom - size.Height - (int)(BottomGap * screen.Scaling));
    }
}
