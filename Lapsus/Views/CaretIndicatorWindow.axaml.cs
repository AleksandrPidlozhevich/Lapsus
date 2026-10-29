using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Views;

public partial class CaretIndicatorWindow : Window
{
    private static readonly IBrush Listening = new SolidColorBrush(Color.Parse("#E5484D"));
    private static readonly IBrush Working = new SolidColorBrush(Color.Parse("#8B8D98"));

    private readonly TextBlock _label;
    private readonly Canvas _mic;
    private readonly Path _micHead;
    private readonly Path _micStand;

    public CaretIndicatorWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _label = this.FindControl<TextBlock>("Label")
                 ?? throw new InvalidOperationException("CaretIndicatorWindow.axaml has no Label.");
        _mic = this.FindControl<Canvas>("Mic")
               ?? throw new InvalidOperationException("CaretIndicatorWindow.axaml has no Mic.");
        _micHead = this.FindControl<Path>("MicHead")!;
        _micStand = this.FindControl<Path>("MicStand")!;
    }

    public string Text
    {
        get => _label.Text ?? string.Empty;
        set
        {
            _label.Text = value;
            _label.IsVisible = value.Length > 0;
        }
    }

    // null: the plain text badge (layout indicator); true: the microphone listening; false: working on it.
    public bool? Microphone
    {
        set
        {
            _mic.IsVisible = value is not null;
            var brush = value == true ? Listening : Working;
            _micHead.Fill = brush;
            _micStand.Stroke = brush;
            _mic.Opacity = 1;
        }
    }

    // Loudness 0..1: a quiet microphone is dimmer, a voice lights it up.
    public void ShowLevel(float level)
    {
        _mic.Opacity = 0.45 + 0.55 * Math.Clamp(level * 12, 0, 1);
    }

    public PixelPoint NativePosition
    {
        get
        {
            if (OperatingSystem.IsWindows()
                && TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero
                && GetWindowRect(hwnd, out var rect))
                return new PixelPoint(rect.left, rect.top);

            return Position;
        }
    }

    public void MoveTo(PixelPoint position)
    {
        Position = position;
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle()?.Handle is not { } hwnd
                                         || hwnd == IntPtr.Zero)
            return;

        SetWindowPos(hwnd, IntPtr.Zero, position.X, position.Y, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (OperatingSystem.IsMacOS())
        {
            MacOverlayWindow.Apply(this);
            return;
        }

        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle()?.Handle is not { } hwnd
                                         || hwnd == IntPtr.Zero)
            return;

        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE,
            new IntPtr(style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
}
