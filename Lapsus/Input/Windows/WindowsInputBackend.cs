using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Input;

[SupportedOSPlatform("windows")]
internal sealed class WindowsInputBackend : InputBackendBase<TypingFocus>
{

    private const int CopyTimeoutMs = 500;

    private const int CopyPollMs = 10;

    private const int ReadAttempts = 3;

    private const int RestoreDelayMs = 500;

    private const int RestoreSettleMs = 150;

    private readonly WindowsFocusProbe _focus = new();

    // Keep the hook delegate alive for process life (otherwise GC → crash).
    private readonly LowLevelKeyboardProc _proc;
    private readonly LowLevelMouseProc _mouseProc;
    private readonly WinEventDelegate _winEventProc;
    private IntPtr _mouseHookHandle;
    private IntPtr _hookHandle;
    private IntPtr _foregroundEventHook;
    private IntPtr _focusEventHook;

    private bool _shiftDown;
    private bool _ctrlDown;
    private bool _altDown;

    private bool _rightAltDown;

    private bool _winDown;

    private uint _swallowedKeyDown;

    private Action? _pendingHotkeyAction;

    private readonly byte[] _keyState = new byte[256];
    private readonly StringBuilder _translateBuffer = new(8);
    private readonly char[] _translated = new char[8];

    private bool _deadKeyPending;

    private char _pendingAccent;

    public WindowsInputBackend(IPhraseCorrector corrector, AppExclusions? excludedApps = null)
        : base(corrector, excludedApps)
    {
        HotkeyVirtualKey = VK_PAUSE;
        _proc = HookCallback;
        _mouseProc = MouseHookCallback;
        _winEventProc = OnWinEvent;
    }

    public override bool IsRunning => _hookHandle != IntPtr.Zero;

    public override bool SkipPasswordFields
    {
        get => _focus.SkipPasswordFields;
        set => _focus.SkipPasswordFields = value;
    }

    public override void Start()
    {
        if (IsRunning)
            return;

        var module = GetModuleHandle(null);
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, module, 0);
        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"SetWindowsHookEx failed (error {Marshal.GetLastWin32Error()}).");

        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);

        // Hook callbacks run on the UI thread; a stall stalls the keyboard (Windows unhooks on timeout).
        _foregroundEventHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        _focusEventHook = SetWinEventHook(
            EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        ClearTypingContext(default);
        ResetModifierState();
        _focus.Reset();
    }

    public override void Stop()
    {
        if (!IsRunning)
            return;

        if (_foregroundEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundEventHook);
            _foregroundEventHook = IntPtr.Zero;
        }

        if (_focusEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_focusEventHook);
            _focusEventHook = IntPtr.Zero;
        }

        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        ClearTypingContext(default);
        ResetModifierState();
        _focus.Reset();
    }

    private void ResetModifierState()
    {
        _shiftDown = _ctrlDown = _altDown = _rightAltDown = _winDown = false;
        _deadKeyPending = false;
        _pendingAccent = '\0';
        _swallowedKeyDown = 0;
        _pendingHotkeyAction = null;
    }

    protected override List<InstalledLayout> EnumerateLayouts()
    {
        return InstalledLayouts.Enumerate().ToList();
    }

    protected override InstalledLayout? FindActiveLayout(IReadOnlyList<InstalledLayout> layouts)
    {
        var activeHkl = ForegroundKeyboardLayout();
        return layouts.FirstOrDefault(l => l.Hkl == activeHkl);
    }

    protected override bool TryInject(int backspaces, string text)
    {
        if (WindowsTextInjection.ReplaceTrailing(backspaces, text))
            return true;

        RaiseDiagnosticFormat("Diag_SendFailed",
            0, backspaces * 2, 0, text.Length * 2,
            Marshal.GetLastWin32Error(), WindowsFocusProbe.ForegroundWindowName());
        return false;
    }

    protected override void ApplyLayoutSwitch(KeyboardLayout? target, string? layoutId)
    {
        if (WindowsLayoutSwitch.Apply(target, layoutId, ref Layouts) is { } notInstalled)
            RaiseDiagnostic(notInstalled);
    }

    protected override string ForegroundAppName()
    {
        return WindowsFocusProbe.ForegroundWindowName();
    }

    protected override string? ForegroundProcessName()
    {
        return _focus.ForegroundProcessName();
    }

    protected override string? PlatformInputBlockedReason()
    {

        if (_focus.IsPassword(_focus.Read().Focus))
            return Localizer.Instance["Diag_SecureField"];

        if (InstalledLayouts.IsImeLanguage(ForegroundKeyboardLayout()))
            return Localizer.Instance["Diag_ImeActive"];

        return null;
    }

    protected override string? TypingBlockedReason()
    {

        if (DiscardBufferIfFocusChanged())
            return Localizer.Instance.Format("Diag_FocusChanged", WindowsFocusProbe.ForegroundWindowName());

        return InputBlockedReason();
    }

    private WindowsClipboard.Snapshot? _savedClipboard;

    private bool _clipboardDirty;

    protected override async Task<string?> CopySelectionAsync()
    {
        _savedClipboard = WindowsClipboard.CaptureSnapshot();
        _clipboardDirty = false;

        var before = WindowsClipboard.SequenceNumber();
        if (!WindowsTextInjection.SendCtrlChord(VK_C))
            return null;

        for (var waited = 0; waited < CopyTimeoutMs; waited += CopyPollMs)
        {

            await Task.Delay(CopyPollMs).ConfigureAwait(true);
            if (WindowsClipboard.SequenceNumber() == before)
                continue;

            _clipboardDirty = true;

            for (var attempt = 0; attempt < ReadAttempts; attempt++)
            {
                if (WindowsClipboard.GetText() is { Length: > 0 } text)
                    return text;

                await Task.Delay(CopyPollMs).ConfigureAwait(true);
            }

            return null;
        }

        return null;
    }

    protected override bool PasteText(string text)
    {
        if (!WindowsClipboard.SetText(text))
        {
            RaiseDiagnosticFormat("Diag_ClipboardFailed", Marshal.GetLastWin32Error().ToString());
            return false;
        }

        _clipboardDirty = true;
        if (WindowsTextInjection.SendCtrlChord(VK_V))
            return true;

        RaiseDiagnosticFormat("Diag_ClipboardFailed", Marshal.GetLastWin32Error().ToString());
        return false;
    }

    protected override async Task RestoreClipboardAsync()
    {
        if (!_clipboardDirty)
        {
            _savedClipboard = null;
            return;
        }

        _clipboardDirty = false;
        var saved = _savedClipboard;
        _savedClipboard = null;

        await Task.Delay(RestoreDelayMs).ConfigureAwait(true);
        WindowsClipboard.RestoreSnapshot(saved);

        var afterRestore = WindowsClipboard.SequenceNumber();
        await Task.Delay(RestoreSettleMs).ConfigureAwait(true);
        if (WindowsClipboard.SequenceNumber() != afterRestore)
            WindowsClipboard.RestoreSnapshot(saved);
    }

    protected override bool IsSameContainer(TypingFocus owner, TypingFocus focus)
    {
        return owner.Active == focus.Active;
    }

    protected override bool IsStillFocused(TypingFocus ownerAtStart)
    {
        if (ownerAtStart.IsEmpty)
            return false;

        var focus = _focus.Read();
        if (focus.IsEmpty)
            return false;
        if (_focus.IsTransient(focus.Focus))
            return true;

        return focus.Active == ownerAtStart.Active;
    }

    // Exception across the WH_KEYBOARD_LL callback unwinds into user32; catch, log, pass through.
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return HookCallbackCore(nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            CrashLog.Log(ex, "WH_KEYBOARD_LL hook callback");
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
    }

    private IntPtr HookCallbackCore(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var msg = (int)wParam;
        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        if (data.dwExtraInfo == WindowsTextInjection.Marker)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        var isUp = msg is WM_KEYUP or WM_SYSKEYUP;
        var vk = data.vkCode;

        TrackModifiers(vk, isDown, isUp);

        if (ModifierOf(vk) is { } modifier)
        {
            if (isDown || isUp)
                NoteModifierKey(modifier, isDown);

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (isDown)
        {
            NoteNonModifierInput();

            if (TryDispatchHotkey(vk))
            {
                _swallowedKeyDown = vk;
                return 1;
            }

            CaptureKey(vk, data.scanCode);
        }
        else if (isUp && _swallowedKeyDown == vk)
        {

            _swallowedKeyDown = 0;
            var run = _pendingHotkeyAction;
            _pendingHotkeyAction = null;
            if (run is not null)
                Dispatcher.UIThread.Post(run);
            return 1;
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
            {
                ResetTypingBuffer();
                NoteNonModifierInput();
            }
        }
        catch (Exception ex)
        {
            CrashLog.Log(ex, "WH_MOUSE_LL hook callback");
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            _focus.NoteWinEvent(eventType, hwnd, idObject, idChild);

            DiscardBufferIfFocusChanged();
        }
        catch (Exception ex)
        {
            CrashLog.Log(ex, "WinEvent hook callback");
        }
    }

    private void TrackModifiers(uint vk, bool isDown, bool isUp)
    {
        switch (vk)
        {
            case VK_SHIFT or VK_LSHIFT or VK_RSHIFT:
                if (isDown) _shiftDown = true;
                else if (isUp) _shiftDown = false;
                break;
            case VK_CONTROL or VK_LCONTROL or VK_RCONTROL:
                if (isDown) _ctrlDown = true;
                else if (isUp) _ctrlDown = false;
                break;
            case VK_MENU or VK_LMENU:
                if (isDown) _altDown = true;
                else if (isUp) _altDown = false;
                break;
            case VK_RMENU:
                if (isDown) _altDown = _rightAltDown = true;
                else if (isUp) _altDown = _rightAltDown = false;
                break;
            case VK_LWIN or VK_RWIN:
                if (isDown) _winDown = true;
                else if (isUp) _winDown = false;
                break;
        }
    }

    private bool TryDispatchHotkey(uint vk)
    {
        Action? run = null;
        if ((int)vk == HotkeyVirtualKey)
            run = ApplyCorrection;
        else if (SelectionActionFor((int)vk) is { } action)
            run = () => _ = ApplySelectionActionAsync(action);

        if (run is null)
            return false;

        if (IsShortcutModifierDown)
            return false;

        if (IsExcludedApp(out _))
            return false;

        _pendingHotkeyAction = run;
        return true;
    }

    private static TapModifier? ModifierOf(uint vk)
    {
        return vk switch
        {
            VK_SHIFT or VK_LSHIFT or VK_RSHIFT => TapModifier.Shift,
            VK_CONTROL or VK_LCONTROL or VK_RCONTROL => TapModifier.Control,
            VK_MENU or VK_LMENU or VK_RMENU => TapModifier.Alt,
            _ => null
        };
    }

    private bool IsShortcutModifierDown => (_ctrlDown || _altDown || _winDown) && !_rightAltDown;

    private static bool IsCaretMovingKey(uint vk)
    {
        return vk is VK_TAB or VK_ESCAPE
            or VK_PRIOR or VK_NEXT or VK_END or VK_HOME
            or VK_LEFT or VK_UP or VK_RIGHT or VK_DOWN
            or VK_INSERT or VK_DELETE;
    }

    private void CaptureKey(uint vk, uint scanCode)
    {
        DiscardBufferIfFocusChanged();
        DropTypingBufferIfIdle();

        EndCorrectionCycle();

        if (IsExcludedApp(out _))
        {
            ResetTypingBuffer();
            return;
        }

        if (_focus.IsPassword(BufferOwner.Focus))
        {
            ResetTypingBuffer();
            return;
        }

        var layout = ForegroundKeyboardLayout();
        if (InstalledLayouts.IsImeLanguage(layout))
        {
            ResetTypingBuffer();
            return;
        }

        if (IsCaretMovingKey(vk))
        {
            ResetTypingBuffer();
            return;
        }

        if (vk == VK_BACK)
        {

            BackspaceTypingBuffer(IsShortcutModifierDown);
            return;
        }

        if (IsShortcutModifierDown)
        {
            ResetTypingBuffer();
            return;
        }

        NoteKeystrokeLayout(layout.ToInt64());

        AppendCapturedText(_translated.AsSpan(0, Translate(vk, scanCode, layout)));
    }

    private int Translate(uint vk, uint scanCode, IntPtr layout)
    {
        _keyState[VK_SHIFT] = _shiftDown ? (byte)0x80 : (byte)0x00;
        _keyState[VK_CAPITAL] = (GetKeyState(VK_CAPITAL) & 1) != 0 ? (byte)0x01 : (byte)0x00;

        _keyState[VK_CONTROL] = _rightAltDown ? (byte)0x80 : (byte)0x00;
        _keyState[VK_MENU] = _rightAltDown ? (byte)0x80 : (byte)0x00;

        var sb = _translateBuffer;
        sb.Clear();

        var rc = ToUnicodeEx(vk, scanCode, _keyState, sb, sb.Capacity, 0x4, layout);

        if (rc < 0)
        {

            _deadKeyPending = true;
            _pendingAccent = sb.Length > 0 ? sb[0] : '\0';
            return 0;
        }

        var afterDeadKey = _deadKeyPending;
        var accent = _pendingAccent;
        _deadKeyPending = false;
        _pendingAccent = '\0';

        if (rc == 0 || sb.Length == 0)
            return 0;

        if (rc == 1)
        {

            _translated[0] = afterDeadKey && Accented(sb[0], accent) is { } composed ? composed : sb[0];
            return 1;
        }

        var count = Math.Min(sb.Length, _translated.Length);
        if (afterDeadKey)
        {
            var letter = sb[count - 1];
            _translated[0] = Accented(letter, accent) ?? letter;
            return 1;
        }

        sb.CopyTo(0, _translated, 0, count);
        return count;
    }

    private static char? Accented(char letter, char accent)
    {
        return accent != '\0' && char.IsLetter(letter) && DeadKeys.CombiningMarks(accent) is { } marks
            ? DeadKeys.Compose(letter, marks)
            : null;
    }

    private bool DiscardBufferIfFocusChanged()
    {
        var focus = _focus.Read();
        return ApplyFocusSnapshot(focus, !focus.IsEmpty && _focus.IsTransient(focus.Focus));
    }
}
