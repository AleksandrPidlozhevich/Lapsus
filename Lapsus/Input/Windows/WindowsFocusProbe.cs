using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Input;

internal readonly record struct TypingFocus(IntPtr Active, IntPtr Focus) : ITypingFocus
{
    public bool IsEmpty => Active == IntPtr.Zero;
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsFocusProbe
{
    private readonly StringBuilder _classNameBuffer = new(256);

    private IntPtr _passwordCheckedFocus = IntPtr.Zero;
    private bool _focusIsPassword;

    private IntPtr _processCheckedWindow = IntPtr.Zero;
    private string? _processName;

    private bool _focusIsProtected;

    public bool SkipPasswordFields { get; set; } = true;

    public void Reset()
    {
        _focusIsProtected = false;
        _passwordCheckedFocus = IntPtr.Zero;
        _focusIsPassword = false;
        _processCheckedWindow = IntPtr.Zero;
        _processName = null;
    }

    public TypingFocus Read()
    {
        var active = GetForegroundWindow();
        if (active == IntPtr.Zero)
            return default;

        var threadId = GetWindowThreadProcessId(active, out _);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        var focus = active;
        if (threadId != 0 && GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
            focus = info.hwndFocus;

        return new TypingFocus(active, focus);
    }

    public bool IsTransient(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return true;

        _classNameBuffer.Clear();
        if (GetClassName(hwnd, _classNameBuffer, _classNameBuffer.Capacity) <= 0)
            return false;

        var className = _classNameBuffer.ToString();
        return className is "#32768" or "tooltips_class32" or "IME" or "MSCTFIME UI";
    }

    public void NoteWinEvent(uint eventType, IntPtr hwnd, int idObject, int idChild)
    {
        if (eventType == EVENT_OBJECT_FOCUS)
            _focusIsProtected = SkipPasswordFields
                                && WindowsSecureInput.IsProtectedAccessibleObject(hwnd, idObject, idChild);
        else if (eventType == EVENT_SYSTEM_FOREGROUND)
            _focusIsProtected = false;
        else
            return;

        _passwordCheckedFocus = IntPtr.Zero;
        _focusIsPassword = false;
    }

    public bool IsPassword(IntPtr focus)
    {
        if (!SkipPasswordFields)
            return false;

        if (_focusIsProtected)
            return true;

        if (focus != _passwordCheckedFocus)
        {
            _passwordCheckedFocus = focus;

            _focusIsPassword = WindowsSecureInput.IsPasswordField(focus)
                               || WindowsSecureInput.IsProtectedFocusedElement(focus);
        }

        return _focusIsPassword;
    }

    public string? ForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        if (hwnd == _processCheckedWindow)
            return _processName;

        _processCheckedWindow = hwnd;
        _processName = ReadProcessName(hwnd);
        return _processName;
    }

    private static string? ReadProcessName(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;

        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(260);
            var size = buffer.Capacity;
            if (!QueryFullProcessImageName(process, 0, buffer, ref size) || size <= 0)
                return null;

            var path = buffer.ToString(0, size);
            var name = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrEmpty(name) ? null : name;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static string ForegroundWindowName()
    {
        var sb = new StringBuilder(256);
        GetClassName(GetForegroundWindow(), sb, sb.Capacity);
        return sb.Length == 0 ? "?" : sb.ToString();
    }
}
