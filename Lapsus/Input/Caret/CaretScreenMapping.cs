using System;

namespace Lapsus.Input;

internal static class CaretScreenMapping
{

    internal const int MaxWidth = 96;

    internal const int MaxHeight = 192;

    internal const int EdgeSlop = 4;

    internal const int MinOwnerHeightForImeDefault = 80;

    internal static CaretBounds Scale(CaretBounds client, double scale)
    {
        if (scale <= 0 || Math.Abs(scale - 1) < 0.001)
            return client;

        return new CaretBounds(
            (int)Math.Round(client.Left * scale),
            (int)Math.Round(client.Top * scale),
            (int)Math.Round(client.Right * scale),
            (int)Math.Round(client.Bottom * scale));
    }

    internal static bool IsPlausibleSliver(CaretBounds caret)
    {
        var width = caret.Right - caret.Left;
        var height = caret.Bottom - caret.Top;
        return height > 0 && height <= MaxHeight && width >= 0 && width <= MaxWidth;
    }

    internal static CaretBounds MapToScreen(
        CaretBounds client, int originX, int originY, int clientWidth, int clientHeight)
    {
        if (!IsPlausibleSliver(client) || !FitsOwner(client, clientWidth, clientHeight))
            return default;

        return new CaretBounds(
            originX + client.Left,
            originY + client.Top,
            originX + client.Right,
            originY + client.Bottom);
    }

    internal static bool FitsOwner(CaretBounds client, int clientWidth, int clientHeight)
    {
        return client.Left >= -EdgeSlop
               && client.Top >= -EdgeSlop
               && client.Right <= clientWidth + EdgeSlop
               && client.Bottom <= clientHeight + EdgeSlop;
    }

    internal static bool IsDefaultImeAnchor(CaretBounds client, int clientWidth, int clientHeight)
    {
        if (clientHeight < MinOwnerHeightForImeDefault || clientWidth < MinOwnerHeightForImeDefault)
            return false;

        var height = client.Bottom - client.Top;
        return client.Left <= 16 && client.Top >= clientHeight - height - 16;
    }
}
