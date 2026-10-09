using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
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

    // Secure-field checks run on worker threads. Until one finishes, the field counts as protected:
    // the hook must never wait on another process, and an unknown field must not get typed text captured.
    private IntPtr _passwordCheckedFocus = IntPtr.Zero;
    private Task<bool>? _focusCheck;
    private Task<bool>? _eventCheck;
    private (IntPtr Hwnd, int Object, int Child) _eventKey;

    private IntPtr _processCheckedWindow = IntPtr.Zero;
    private string? _processName;

    public bool SkipPasswordFields { get; set; } = true;

    public void Reset()
    {
        _eventKey = default;
        _eventCheck = null;
        _passwordCheckedFocus = IntPtr.Zero;
        _focusCheck = null;
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
        {
            // Focus events repeat for the element already being typed in. Re-checking it would make the
            // field pending mid-word, and a pending field clears the typing buffer, so keep the verdict.
            var key = (hwnd, idObject, idChild);
            if (key == _eventKey && _eventCheck is not null)
                return;

            _eventKey = key;
            _eventCheck = SkipPasswordFields
                ? WindowsSecureInput.IsProtectedAccessibleObject(hwnd, idObject, idChild)
                : null;
            return;
        }

        if (eventType != EVENT_SYSTEM_FOREGROUND)
            return;

        _eventKey = default;
        _eventCheck = null;
        _passwordCheckedFocus = IntPtr.Zero;
        _focusCheck = null;
    }

    // Called from the keyboard hook and the WinEvent handler, so it never blocks: an unfinished check reads as protected.
    public bool IsPassword(IntPtr focus)
    {
        if (!SkipPasswordFields)
            return false;

        EnsureFocusCheckStarted(focus);

        if (_eventCheck is { } eventCheck && (!eventCheck.IsCompleted || eventCheck.Result))
            return true;

        return !_focusCheck!.IsCompleted || _focusCheck.Result;
    }

    // Same check, but a still-pending result reads as "not yet confirmed" rather than "protected".
    // Used while capturing keystrokes: failing closed there would silently drop the letters typed
    // before a slow (MSAA, hundreds of milliseconds) check resolves, even in an ordinary field.
    // IsPassword stays fail-closed for the hotkey/diagnostics path, where a still-pending read only
    // delays an about-to-be-confirmed field's correction by that same fraction of a second.
    public bool IsConfirmedPassword(IntPtr focus)
    {
        if (!SkipPasswordFields)
            return false;

        EnsureFocusCheckStarted(focus);

        if (_eventCheck is { IsCompleted: true, Result: true })
            return true;

        return _focusCheck is { IsCompleted: true, Result: true };
    }

    private void EnsureFocusCheckStarted(IntPtr focus)
    {
        if (_focusCheck is not null && focus == _passwordCheckedFocus)
            return;

        _passwordCheckedFocus = focus;
        _focusCheck = WindowsSecureInput.IsPasswordField(focus)
            ? Task.FromResult(true)
            : WindowsSecureInput.IsProtectedFocusedElement(focus);
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
