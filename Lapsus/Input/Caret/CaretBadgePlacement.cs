using System;
using Avalonia;

namespace Lapsus.Input;

internal static class CaretBadgePlacement
{

    private const double GapXDip = 8;

    private const double GapYDip = 2;

    public static PixelPoint For(CaretBounds caret, PixelSize badge, PixelRect workArea, double scale)
    {
        var gapX = ToPixels(GapXDip, scale);
        var gapY = ToPixels(GapYDip, scale);

        var x = caret.Right + gapX;
        var y = caret.Bottom + gapY;

        if (badge.Width <= 0 || badge.Height <= 0 || workArea.Width <= 0 || workArea.Height <= 0)
            return new PixelPoint(x, y);

        if (x + badge.Width > workArea.Right)
            x = caret.Left - gapX - badge.Width;

        if (y + badge.Height > workArea.Bottom)
            y = caret.Top - gapY - badge.Height;

        return new PixelPoint(
            Clamp(x, workArea.X, workArea.Right - badge.Width),
            Clamp(y, workArea.Y, workArea.Bottom - badge.Height));
    }

    private static int ToPixels(double dip, double scale)
    {
        return (int)Math.Round(dip * (scale > 0 ? scale : 1));
    }

    private static int Clamp(int value, int min, int max)
    {
        return max <= min ? min : Math.Clamp(value, min, max);
    }
}
