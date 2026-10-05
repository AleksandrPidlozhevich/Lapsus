using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Views;

public partial class CaretIndicatorWindow : Window
{
    private readonly TextBlock _label;

    public CaretIndicatorWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _label = this.FindControl<TextBlock>("Label")
                 ?? throw new InvalidOperationException("CaretIndicatorWindow.axaml has no Label.");
    }

    public string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
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
