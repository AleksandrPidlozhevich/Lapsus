using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace Lapsus.Input;

[SupportedOSPlatform("windows")]
// Password-field skip: ES_PASSWORD / STATE_SYSTEM_PROTECTED; idle-drop is the backstop.
internal static class WindowsSecureInput
{
    private const int GwlStyle = -16;
    private const int EsPassword = 0x0020;
    private const int StateSystemProtected = 0x20000000;
    private const uint ObjidClient = 0xFFFFFFFC;
    private static readonly object ChildIdSelf = 0;
    private static readonly Guid IidAccessible = new("618736e0-3c3d-11cf-810c-00aa00389b71");

    public static bool IsPasswordField(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        if (!IsEditClass(hwnd))
            return false;

        return (GetWindowLong(hwnd, GwlStyle) & EsPassword) != 0;
    }

    // Off the calling thread: MSAA/COM calls into other processes can take hundreds of milliseconds.
    public static Task<bool> IsProtectedAccessibleObject(IntPtr hwnd, int idObject, int idChild)
    {
        return Task.Run(() =>
        {
            try
            {
                if (AccessibleObjectFromEvent(hwnd, (uint)idObject, (uint)idChild, out var accessible, out var child) !=
                    0
                    || accessible is null)
                    return false;

                return IsProtected(accessible, child);
            }
            catch
            {

                return false;
            }
        });
    }

    public static Task<bool> IsProtectedFocusedElement(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return Task.FromResult(false);

        return Task.Run(() =>
        {
            try
            {
                var iid = IidAccessible;
                if (AccessibleObjectFromWindow(hwnd, ObjidClient, ref iid, out var client) != 0 || client is null)
                    return false;

                if (client.get_accFocus(out var focused) != 0)
                    return false;

                return focused switch
                {
                    IAccessible element => IsProtected(element, ChildIdSelf),
                    int childId => IsProtected(client, childId),
                    _ => false
                };
            }
            catch
            {

                return false;
            }
        });
    }

    private static bool IsProtected(IAccessible accessible, object childId)
    {
        return accessible.get_accState(childId, out var state) == 0
               && state is int flags
               && (flags & StateSystemProtected) != 0;
    }

    private static bool IsEditClass(IntPtr hwnd)
    {
        var buffer = new StringBuilder(64);
        if (GetClassName(hwnd, buffer, buffer.Capacity) <= 0)
            return false;

        return buffer.ToString().Contains("edit", StringComparison.OrdinalIgnoreCase);
    }

    [ComImport]
    [Guid("618736e0-3c3d-11cf-810c-00aa00389b71")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessible
    {
        [PreserveSig]
        int get_accParent([MarshalAs(UnmanagedType.IDispatch)] out object? parent);

        [PreserveSig]
        int get_accChildCount(out int count);

        [PreserveSig]
        int get_accChild([MarshalAs(UnmanagedType.Struct)] object child,
            [MarshalAs(UnmanagedType.IDispatch)] out object? result);

        [PreserveSig]
        int get_accName([MarshalAs(UnmanagedType.Struct)] object child,
            [MarshalAs(UnmanagedType.BStr)] out string? name);

        [PreserveSig]
        int get_accValue([MarshalAs(UnmanagedType.Struct)] object child,
            [MarshalAs(UnmanagedType.BStr)] out string? value);

        [PreserveSig]
        int get_accDescription([MarshalAs(UnmanagedType.Struct)] object child,
            [MarshalAs(UnmanagedType.BStr)] out string? description);

        [PreserveSig]
        int get_accRole([MarshalAs(UnmanagedType.Struct)] object child,
            [MarshalAs(UnmanagedType.Struct)] out object? role);

        [PreserveSig]
        int get_accState([MarshalAs(UnmanagedType.Struct)] object child,
            [MarshalAs(UnmanagedType.Struct)] out object? state);

        [PreserveSig]
        int get_accHelp([MarshalAs(UnmanagedType.Struct)] object child,
            [MarshalAs(UnmanagedType.BStr)] out string? help);

        [PreserveSig]
        int get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string? helpFile,
            [MarshalAs(UnmanagedType.Struct)] object child, out int topic);

        [PreserveSig]
        int get_accKeyboardShortcut([MarshalAs(UnmanagedType.Struct)] object child,
            [MarshalAs(UnmanagedType.BStr)] out string? shortcut);

        [PreserveSig]
        int get_accFocus([MarshalAs(UnmanagedType.Struct)] out object? focus);
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr hwnd, uint dwId, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? ppvObject);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromEvent(
        IntPtr hwnd, uint dwId, uint dwChildId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? ppacc,
        [MarshalAs(UnmanagedType.Struct)] out object pvarChild);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
