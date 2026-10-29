using Avalonia.Controls;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Updates;

// Avalonia's TrayIcon has no notification API, so this adds a balloon (shown as a toast on Windows 10/11)
// to the icon Avalonia already registered: same window handle and id, NIM_MODIFY with only NIF_INFO.
// Finding that handle and id needs Avalonia internals; if they move, TryShow returns false and the tray
// still has its menu item, tooltip and icon dot. AppUpdateNoticeTests guards the internals.
internal static class WindowsTrayBalloon
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private const uint NIM_MODIFY = 0x1;
    private const uint NIF_INFO = 0x10;
    private const uint NIIF_NOSOUND = 0x10;
    private const uint NIIF_RESPECT_QUIET_TIME = 0x80;

    [SupportedOSPlatform("windows")]
    public static bool TryShow(TrayIcon trayIcon, string title, string text)
    {
        try
        {
            if (!TryLocate(trayIcon, out var hwnd, out var id))
                return false;

            var data = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = hwnd,
                uID = id,
                uFlags = NIF_INFO,
                szTip = string.Empty,
                szInfo = Truncate(text, 255),
                szInfoTitle = Truncate(title, 63),
                // No sound, and Windows holds it back during quiet time (full-screen apps, focus assist).
                dwInfoFlags = NIIF_NOSOUND | NIIF_RESPECT_QUIET_TIME
            };
            return Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
        catch (Exception ex)
        {
            CrashLog.Log(ex, "update balloon");
            return false;
        }
    }

    private static bool TryLocate(TrayIcon trayIcon, out IntPtr hwnd, out uint id)
    {
        hwnd = IntPtr.Zero;
        id = 0;

        var impl = typeof(TrayIcon).GetProperty("Impl", Any)?.GetValue(trayIcon);
        if (impl?.GetType().GetField("_uniqueId", Any)?.GetValue(impl) is not int uniqueId)
            return false;

        var platform = PlatformType(impl.GetType().Assembly);
        var instance = platform?.GetProperty("Instance", AnyStatic)?.GetValue(null);
        if (instance is null || platform!.GetProperty("Handle", Any)?.GetValue(instance) is not IntPtr handle
                             || handle == IntPtr.Zero)
            return false;

        hwnd = handle;
        id = (uint)uniqueId;
        return true;
    }

    private static Type? PlatformType(Assembly win32) => win32.GetType("Avalonia.Win32.Win32Platform");

    // The members TryLocate reads, checked without creating a tray icon (tests, any OS).
    internal static bool AvaloniaInternalsPresent()
    {
        var impl = Type.GetType("Avalonia.Win32.TrayIconImpl, Avalonia.Win32");
        var platform = impl is null ? null : PlatformType(impl.Assembly);
        return typeof(TrayIcon).GetProperty("Impl", Any) is not null
               && impl?.GetField("_uniqueId", Any)?.FieldType == typeof(int)
               && platform?.GetProperty("Instance", AnyStatic) is not null
               && platform.GetProperty("Handle", Any)?.PropertyType == typeof(IntPtr);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [SupportedOSPlatform("windows")]
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
}
