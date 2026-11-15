using System;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Input;

// A caret-sized rectangle at the mouse pointer: where a badge goes in an app that reports no caret.
internal static class PointerAnchor
{
    // Null where the pointer cannot be read.
    public static CaretBounds? Read()
    {
        if (OperatingSystem.IsWindows())
            return GetCursorPos(out var point) ? At(point.x, point.y) : null;

        return OperatingSystem.IsMacOS() && MacPointer.Location() is { } at ? At(at.X, at.Y) : null;
    }

    // As tall as a line of text, so the badge sits beside the pointer the way it sits beside a caret.
    private static CaretBounds At(int x, int y) => new(x, y, x + 1, y + 18);
}
