using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Input;

// Where the mouse pointer is, in global display points with the origin at the top left — the space the
// caret probe reports in. Needs no permission, so it works in the App Store sandbox as well.
[SupportedOSPlatform("macos")]
internal static class MacPointer
{
    public static (int X, int Y)? Location()
    {
        var @event = CGEventCreate(IntPtr.Zero);
        if (@event == IntPtr.Zero)
            return null;

        try
        {
            var point = CGEventGetLocation(@event);
            return ((int)Math.Round(point.X), (int)Math.Round(point.Y));
        }
        finally
        {
            MacOSNativeMethods.CFRelease(@event);
        }
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern MacOSNativeMethods.CGPoint CGEventGetLocation(IntPtr @event);
}
