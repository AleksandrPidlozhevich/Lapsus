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

    private readonly WindowsModifierKeys _modifiers = new();

    private readonly WindowsKeyTranslator _translator = new();

    private uint _swallowedKeyDown;

    private Action? _pendingHotkeyAction;

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
        _modifiers.Reset();
        _translator.Reset();
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

    protected override string? PlatformInputBlockedReason(bool passwords)
    {

        if (passwords && _focus.IsPassword(_focus.Read().Focus))
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

    protected override TypingFocus CurrentFocus() => _focus.Read();

    protected override bool SetClipboardText(string text) => WindowsClipboard.SetText(text);

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

        _modifiers.Track(vk, isDown, isUp);

        if (IsCapturingHotkey && FeedHotkeyCapture((int)vk, isDown, WindowsModifierKeys.KeyKindOf(vk), _modifiers.Held()))
            return 1;

        var isWin = vk is VK_LWIN or VK_RWIN;
        if (isDown || isUp)
        {
            if (WindowsModifierKeys.ModifierOf(vk) is not null || isWin)
            {
                // Win let go after Ctrl+Win would open Start; an unassigned key in between tells Windows it was a chord.
                if (NoteDictationModifiers(_modifiers.Held()) && _modifiers.Win)
                    WindowsTextInjection.MaskStartMenu();
            }
            else if (isDown && _swallowedKeyDown != vk)
            {
                NoteDictationOtherKey();
            }
        }

        // A held hotkey repeats its key-down; it already did its work on the first one.
        if (isDown && _swallowedKeyDown == vk)
            return 1;

        if (WindowsModifierKeys.ModifierOf(vk) is { } modifier)
        {
            if (isDown || isUp)
                NoteModifierKey(modifier, isDown);

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (isDown)
        {
            NoteNonModifierInput();

            // Esc during a recording cancels it and goes no further: the app behind must not see it.
            if (vk == VK_ESCAPE && DictationListening)
            {
                _swallowedKeyDown = vk;
                _pendingHotkeyAction = RaiseDictationCancel;
                return 1;
            }

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

    private bool TryDispatchHotkey(uint vk)
    {
        var held = _modifiers.Held();
        var plainAllowed = !_modifiers.IsShortcutDown;

        Action? run = null;
        var dictation = false;
        if (TriggerMatches(HotkeyVirtualKey, (int)vk, held, plainAllowed))
            run = ApplyCorrection;
        else if (SelectionActionForKey((int)vk, held, plainAllowed) is { } action)
            run = () => _ = ApplySelectionActionAsync(action);
        else if (TriggerMatches(DictationHotkey, (int)vk, held, plainAllowed))
            (run, dictation) = (() => RaiseDictationReleased(), true);

        if (run is null)
            return false;

        if (IsExcludedApp(out _))
            return false;

        // Dictation starts on the key going down, so it can be held while speaking; the rest wait for key-up.
        if (dictation)
            Dispatcher.UIThread.Post(RaiseDictationHotkey);
        _pendingHotkeyAction = run;
        return true;
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

        // Not IsPassword: that fails closed while the (sometimes hundreds-of-milliseconds) accessibility
        // check is still pending, which would drop every letter typed right after focusing an ordinary
        // field. The hotkey/diagnostics path still fails closed on a merely-pending check; this one only
        // stops tracking once the field is actually confirmed protected.
        if (_focus.IsConfirmedPassword(BufferOwner.Focus))
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

        if (WindowsModifierKeys.IsCaretMovingKey(vk))
        {
            ResetTypingBuffer();
            return;
        }

        if (vk == VK_BACK)
        {

            BackspaceTypingBuffer(_modifiers.IsShortcutDown);
            return;
        }

        if (_modifiers.IsShortcutDown)
        {
            ResetTypingBuffer();
            return;
        }

        NoteKeystrokeLayout(layout.ToInt64());

        AppendCapturedText(_translator.Translate(vk, scanCode, layout, _modifiers.Shift, _modifiers.RightAlt));
    }

    private bool DiscardBufferIfFocusChanged()
    {
        var focus = _focus.Read();
        return ApplyFocusSnapshot(focus, !focus.IsEmpty && _focus.IsTransient(focus.Focus));
    }
}
