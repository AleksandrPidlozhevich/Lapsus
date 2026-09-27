using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Localization;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal sealed class MacOSInputBackend : InputBackendBase<MacTypingFocus>
{

    private static readonly TimeSpan TapThreadJoin = TimeSpan.FromSeconds(2);

    private readonly MacOSNativeMethods.EventTapCallback _callback;

    private GCHandle _callbackHandle;

    private IntPtr _eventTap;
    private IntPtr _runLoop;
    private Thread? _tapThread;
    private Exception? _startError;
    private readonly ManualResetEventSlim _tapReady = new(false);
    private int _lastForegroundPid = -1;

    private ushort? _swallowedKeyDown;

    private Action? _pendingHotkeyAction;

    private bool _capturing;

    public MacOSInputBackend(IPhraseCorrector corrector, AppExclusions? excludedApps = null)
        : base(corrector, excludedApps)
    {
        HotkeyVirtualKey = MacHotkeys.CtrlOptionSpace;
        _callback = EventTapHandler;
        _callbackHandle = GCHandle.Alloc(_callback);
    }

    public override bool IsRunning => Volatile.Read(ref _capturing);

    public override bool SkipPasswordFields { get; set; } = true;

    public override void Start()
    {
        if (IsRunning)
            return;

        MacAccessibility.PromptIfNeeded();
        MacAccessibility.RequestInputMonitoringIfNeeded();

        _tapReady.Reset();
        _startError = null;
        _tapThread = new Thread(RunTapThread)
        {
            IsBackground = true,
            Name = "Lapsus CGEventTap"
        };
        _tapThread.Start();

        if (!_tapReady.Wait(TimeSpan.FromSeconds(3)))
            throw new InvalidOperationException(Localizer.Instance["Status_AccessibilityRequired"]);

        if (_startError is { } error)
            throw error;

        if (Volatile.Read(ref _eventTap) == IntPtr.Zero)
            throw new InvalidOperationException(Localizer.Instance["Status_AccessibilityRequired"]);

        Volatile.Write(ref _capturing, true);
    }

    public override void Stop()
    {
        StopCapture();

        _tapThread?.Join(TapThreadJoin);
        _tapThread = null;
        _swallowedKeyDown = null;
        _pendingHotkeyAction = null;
        ForgetTypingContext();
    }

    public override void Dispose()
    {

        StopCapture();
        _tapThread = null;
        ForgetTypingContext();

    }

    private void StopCapture()
    {

        var tap = Volatile.Read(ref _eventTap);
        if (tap != IntPtr.Zero)
            MacOSNativeMethods.CGEventTapEnable(tap, false);

        Volatile.Write(ref _capturing, false);

        var runLoop = Volatile.Read(ref _runLoop);
        if (runLoop != IntPtr.Zero)
            MacOSNativeMethods.CFRunLoopStop(runLoop);
    }

    private void ForgetTypingContext()
    {
        ClearTypingContext(default);
        _lastForegroundPid = -1;
        _lastEventSourcePid = 0;
        lock (_procNameGate)
        {
            _procNameCachedPid = 0;
        }

        MacTypingFocusWatcher.InvalidateFocusCache();
    }

    protected override List<InstalledLayout> EnumerateLayouts()
    {
        return MacInstalledLayouts.Enumerate().ToList();
    }

    protected override InstalledLayout? FindActiveLayout(IReadOnlyList<InstalledLayout> layouts)
    {
        var active = MacOSNativeMethods.TISCopyCurrentKeyboardInputSource();
        if (active == IntPtr.Zero)
            return null;

        try
        {
            var activeId = MacInstalledLayouts.ReadInputSourceId(active);
            return string.IsNullOrEmpty(activeId)
                ? null
                : layouts.FirstOrDefault(l => l.LayoutId == activeId);
        }
        finally
        {
            MacOSNativeMethods.CFRelease(active);
        }
    }

    protected override bool TryInject(int backspaces, string text)
    {
        if (MacTextInjection.ReplaceTrailing(backspaces, text))
            return true;

        RaiseDiagnosticFormat("Diag_InjectFailed", ForegroundAppName());
        return false;
    }

    protected override void ApplyLayoutSwitch(KeyboardLayout? target, string? layoutId)
    {
        if (MacLayoutSwitch.Apply(target, layoutId, ref Layouts) is { } notInstalled)
            RaiseDiagnostic(notInstalled);
    }

    private readonly Lock _procNameGate = new();
    private readonly StringBuilder _procNameBuffer = new(256);
    private int _procNameCachedPid;
    private string _procNameCached = "?";

    protected override string ForegroundAppName()
    {
        var pid = ResolveForegroundPid();
        if (pid <= 0)
            return "?";

        lock (_procNameGate)
        {
            if (pid == _procNameCachedPid)
                return _procNameCached;

            _procNameCached = MacProcessNames.Read(pid, _procNameBuffer) ?? "?";
            _procNameCachedPid = pid;
            return _procNameCached;
        }
    }

    private int ResolveForegroundPid()
    {
        if (_lastForegroundPid > 0)
            return _lastForegroundPid;

        var pid = MacTypingFocusWatcher.ReadFocus(out _).Pid;
        if (pid > 0)
            _lastForegroundPid = pid;

        return pid;
    }

    protected override string? ForegroundProcessName()
    {
        var name = ForegroundAppName();
        return name == "?" ? null : name;
    }

    protected override string? PlatformInputBlockedReason()
    {
        if (SkipPasswordFields && MacOSNativeMethods.IsSecureEventInputEnabled())
            return Localizer.Instance["Diag_SecureField"];

        if (MacInstalledLayouts.CurrentSourceIsInputMethod())
            return Localizer.Instance["Diag_ImeActive"];

        return null;
    }

    protected override string? TypingBlockedReason()
    {
        if (DiscardBufferIfFocusChanged(_lastEventSourcePid))
            return Localizer.Instance.Format("Diag_FocusChanged", ForegroundAppName());

        return InputBlockedReason();
    }

    private const int CopyTimeoutMs = 500;

    private const int CopyPollMs = 10;

    private const int ReadAttempts = 3;

    private const int RestoreDelayMs = 500;

    private const int RestoreSettleMs = 150;

    private MacClipboard.Snapshot? _savedClipboard;

    private bool _clipboardDirty;

    protected override async Task<string?> CopySelectionAsync()
    {
        _savedClipboard = MacClipboard.CaptureSnapshot();
        _clipboardDirty = false;

        MacClipboard.WasModified();
        if (!MacTextInjection.SendCommandChord(MacOSNativeMethods.CKeyCode))
            return null;

        for (var waited = 0; waited < CopyTimeoutMs; waited += CopyPollMs)
        {
            await Task.Delay(CopyPollMs).ConfigureAwait(true);
            if (!MacClipboard.WasModified())
                continue;

            _clipboardDirty = true;

            for (var attempt = 0; attempt < ReadAttempts; attempt++)
            {
                if (MacClipboard.GetText() is { Length: > 0 } copied)
                    return copied;

                await Task.Delay(CopyPollMs).ConfigureAwait(true);
            }

            return null;
        }

        return null;
    }

    protected override bool PasteText(string text)
    {
        if (!MacClipboard.SetText(text))
        {
            RaiseDiagnosticFormat("Diag_ClipboardFailed", ForegroundAppName());
            return false;
        }

        _clipboardDirty = true;
        if (MacTextInjection.SendCommandChord(MacOSNativeMethods.VKeyCode))
            return true;

        RaiseDiagnosticFormat("Diag_ClipboardFailed", ForegroundAppName());
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
        MacClipboard.RestoreSnapshot(saved);

        MacClipboard.WasModified();
        await Task.Delay(RestoreSettleMs).ConfigureAwait(true);
        if (MacClipboard.WasModified())
            MacClipboard.RestoreSnapshot(saved);
    }

    protected override bool IsSameContainer(MacTypingFocus owner, MacTypingFocus focus)
    {
        return owner.Pid == focus.Pid;
    }

    protected override bool IsStillFocused(MacTypingFocus ownerAtStart)
    {
        if (ownerAtStart.IsEmpty)
            return false;

        var focus = MacTypingFocusWatcher.ReadFocus(out var isTransient);
        if (isTransient)
            return true;
        if (focus.IsEmpty)
            return false;

        return focus.Pid == ownerAtStart.Pid;
    }

    private void RunTapThread()
    {
        var runLoop = MacOSNativeMethods.CFRunLoopGetCurrent();
        Volatile.Write(ref _runLoop, runLoop);

        var tap = MacOSNativeMethods.CGEventTapCreate(
            0,
            0,
            0,

            MacOSNativeMethods.EventMaskKeyDown
            | MacOSNativeMethods.EventMaskKeyUp

            | MacOSNativeMethods.EventMaskFlagsChanged
            | MacOSNativeMethods.EventMaskLeftMouseDown
            | MacOSNativeMethods.EventMaskRightMouseDown
            | MacOSNativeMethods.EventMaskOtherMouseDown,
            _callback,
            IntPtr.Zero);

        if (tap == IntPtr.Zero)
        {

            if (Volatile.Read(ref _runLoop) == runLoop)
                Volatile.Write(ref _runLoop, IntPtr.Zero);

            _startError = new InvalidOperationException(Localizer.Instance["Status_AccessibilityRequired"]);
            _tapReady.Set();
            return;
        }

        Volatile.Write(ref _eventTap, tap);

        var source = MacOSNativeMethods.CFMachPortCreateRunLoopSource(IntPtr.Zero, tap, 0);
        MacOSNativeMethods.CFRunLoopAddSource(runLoop, source, MacOSNativeMethods.RunLoopDefaultMode);
        MacOSNativeMethods.CGEventTapEnable(tap, true);

        var watcher = new MacTypingFocusWatcher(OnAccessibilityFocusChanged);
        watcher.TryStart(runLoop);

        _tapReady.Set();

        try
        {
            MacOSNativeMethods.CFRunLoopRun();
        }
        finally
        {
            ReleaseTapResources(runLoop, tap, source, watcher);
        }
    }

    private void OnAccessibilityFocusChanged()
    {
        Dispatcher.UIThread.Post(() => DiscardBufferIfFocusChanged());
    }

    private void ReleaseTapResources(
        IntPtr runLoop, IntPtr tap, IntPtr source, MacTypingFocusWatcher watcher)
    {
        watcher.Dispose();
        MacOSNativeMethods.CFRelease(source);
        MacOSNativeMethods.CGEventTapEnable(tap, false);
        MacOSNativeMethods.CFRelease(tap);

        if (Volatile.Read(ref _eventTap) == tap)
            Volatile.Write(ref _eventTap, IntPtr.Zero);

        if (Volatile.Read(ref _runLoop) == runLoop)
            Volatile.Write(ref _runLoop, IntPtr.Zero);
    }

    // Exception across the CGEventTap callback kills process keyboard; catch, log, pass through.
    private IntPtr EventTapHandler(IntPtr proxy, int type, IntPtr @event, IntPtr userInfo)
    {
        try
        {
            return EventTapHandlerCore(proxy, type, @event, userInfo);
        }
        catch (Exception ex)
        {
            CrashLog.Log(ex, "CGEventTap callback");
            return @event;
        }
    }

    // Tap thread: swallow only; mutate shared buffer state on the UI thread.
    private IntPtr EventTapHandlerCore(IntPtr proxy, int type, IntPtr @event, IntPtr userInfo)
    {
        if (type is MacOSNativeMethods.EventTapDisabledByTimeout
            or MacOSNativeMethods.EventTapDisabledByUserInput)
        {
            var tap = Volatile.Read(ref _eventTap);
            if (tap != IntPtr.Zero && IsRunning)
            {
                MacOSNativeMethods.CGEventTapEnable(tap, true);
                Dispatcher.UIThread.Post(() => RaiseDiagnostic(Localizer.Instance["Diag_TapReenabled"]));
            }

            return @event;
        }

        if (type is MacOSNativeMethods.EventLeftMouseDown
            or MacOSNativeMethods.EventRightMouseDown
            or MacOSNativeMethods.EventOtherMouseDown)
        {
            NoteNonModifierInput();
            Dispatcher.UIThread.Post(ResetTypingBuffer);
            return @event;
        }

        if (type is not (MacOSNativeMethods.EventKeyDown
                or MacOSNativeMethods.EventKeyUp
                or MacOSNativeMethods.EventFlagsChanged)
            || @event == IntPtr.Zero)
            return @event;

        if (MacOSNativeMethods.CGEventGetIntegerValueField(@event, MacOSNativeMethods.EventSourceUserData)
            == MacOSNativeMethods.InjectedMarker)
            return @event;

        var keyCode = (ushort)MacOSNativeMethods.CGEventGetIntegerValueField(
            @event, MacOSNativeMethods.EventKeyboardKeycode);
        var flags = MacOSNativeMethods.CGEventGetFlags(@event);

        var pid = NoteEventProcess(@event);

        if (type == MacOSNativeMethods.EventFlagsChanged)
        {
            if (ModifierOf(keyCode) is { } modifier)
                NoteModifierKey(modifier, (flags & FlagMaskOf(modifier)) != 0);

            return @event;
        }

        if (type == MacOSNativeMethods.EventKeyUp)
        {
            if (_swallowedKeyDown != keyCode)
                return @event;

            _swallowedKeyDown = null;
            var run = _pendingHotkeyAction;
            _pendingHotkeyAction = null;
            if (run is not null)
                Dispatcher.UIThread.Post(run);
            return IntPtr.Zero;
        }

        NoteNonModifierInput();

        if (IsCorrectionHotkey(keyCode, flags))
        {

            if (IsExcludedApp(out _))
                return @event;

            _swallowedKeyDown = keyCode;
            _pendingHotkeyAction = ApplyCorrection;
            return IntPtr.Zero;
        }

        if (!HasMeaningfulModifiers(flags) && SelectionActionFor(keyCode) is { } action)
        {
            if (IsExcludedApp(out _))
                return @event;

            _swallowedKeyDown = keyCode;
            _pendingHotkeyAction = () => _ = ApplySelectionActionAsync(action);
            return IntPtr.Zero;
        }

        var typed = TypedText(@event);
        Dispatcher.UIThread.Post(() => CaptureKey(keyCode, flags, typed, pid));
        return @event;
    }

    private int _lastEventSourcePid;

    private int NoteEventProcess(IntPtr @event)
    {
        var pid = (int)MacOSNativeMethods.CGEventGetIntegerValueField(
            @event, MacOSNativeMethods.EventSourceUnixProcessId);

        _lastEventSourcePid = pid;
        if (pid > 0)
            _lastForegroundPid = pid;

        return pid;
    }

    private bool IsCorrectionHotkey(ushort keyCode, ulong flags)
    {
        if (HotkeyVirtualKey == MacHotkeys.CtrlOptionSpace)
            return keyCode == MacOSNativeMethods.SpaceKeyCode && HasCtrlOption(flags);

        if (HotkeyTriggers.IsDoubleTap(HotkeyVirtualKey))
            return false;

        return keyCode == (ushort)HotkeyVirtualKey && !HasMeaningfulModifiers(flags);
    }

    private static TapModifier? ModifierOf(ushort keyCode)
    {
        return keyCode switch
        {
            MacOSNativeMethods.ShiftKeyCode or MacOSNativeMethods.RightShiftKeyCode => TapModifier.Shift,
            MacOSNativeMethods.ControlKeyCode or MacOSNativeMethods.RightControlKeyCode => TapModifier.Control,
            MacOSNativeMethods.OptionKeyCode or MacOSNativeMethods.RightOptionKeyCode => TapModifier.Alt,
            MacOSNativeMethods.CommandKeyCode or MacOSNativeMethods.RightCommandKeyCode => TapModifier.Command,
            _ => null
        };
    }

    private static ulong FlagMaskOf(TapModifier modifier)
    {
        return modifier switch
        {
            TapModifier.Shift => MacOSNativeMethods.EventFlagMaskShift,
            TapModifier.Control => MacOSNativeMethods.EventFlagMaskControl,
            TapModifier.Alt => MacOSNativeMethods.EventFlagMaskAlternate,
            _ => MacOSNativeMethods.EventFlagMaskCommand
        };
    }

    private static bool HasCtrlOption(ulong flags)
    {
        return (flags & MacOSNativeMethods.EventFlagMaskControl) != 0
               && (flags & MacOSNativeMethods.EventFlagMaskAlternate) != 0
               && (flags & MacOSNativeMethods.EventFlagMaskCommand) == 0;
    }

    private static bool IsCaretMovingKey(ushort keyCode)
    {
        return keyCode is
            MacOSNativeMethods.TabKeyCode or MacOSNativeMethods.EscapeKeyCode
            or MacOSNativeMethods.HomeKeyCode or MacOSNativeMethods.EndKeyCode
            or MacOSNativeMethods.PageUpKeyCode or MacOSNativeMethods.PageDownKeyCode
            or MacOSNativeMethods.LeftArrowKeyCode or MacOSNativeMethods.RightArrowKeyCode
            or MacOSNativeMethods.UpArrowKeyCode or MacOSNativeMethods.DownArrowKeyCode
            or MacOSNativeMethods.ForwardDeleteKeyCode;
    }

    private static bool HasMeaningfulModifiers(ulong flags)
    {
        return (flags & (MacOSNativeMethods.EventFlagMaskControl
                         | MacOSNativeMethods.EventFlagMaskAlternate
                         | MacOSNativeMethods.EventFlagMaskCommand)) != 0;
    }

    private void CaptureKey(ushort keyCode, ulong flags, string typed, int pid)
    {
        DiscardBufferIfFocusChanged(pid);

        DropTypingBufferIfIdle();
        EndCorrectionCycle();

        if (IsExcludedApp(out _))
        {
            ResetTypingBuffer();
            return;
        }

        // Skip when Secure Event Input is on (password fields / Terminal); tap sees no keys.
        if (SkipPasswordFields && MacOSNativeMethods.IsSecureEventInputEnabled())
        {
            ResetTypingBuffer();
            return;
        }

        MacInstalledLayouts.ReadCurrentSource(out var isInputMethod, out var layoutToken);
        if (isInputMethod)
        {
            ResetTypingBuffer();
            return;
        }

        if (IsCaretMovingKey(keyCode))
        {
            ResetTypingBuffer();
            return;
        }

        if (keyCode == MacOSNativeMethods.DeleteKeyCode)
        {

            BackspaceTypingBuffer(HasMeaningfulModifiers(flags));
            return;
        }

        if (HasMeaningfulModifiers(flags))
        {
            ResetTypingBuffer();
            return;
        }

        NoteKeystrokeLayout(layoutToken);

        AppendCapturedText(typed);
    }

    private readonly byte[] _typedTextScratch = new byte[8];
    private readonly char[] _typedTextChars = new char[4];

    private string TypedText(IntPtr @event)
    {
        var scratch = _typedTextScratch;
        MacOSNativeMethods.CGEventKeyboardGetUnicodeString(@event, 4, out var length, scratch);

        var count = Math.Min((int)length, scratch.Length / 2);
        if (count <= 0)
            return string.Empty;

        var text = _typedTextChars;
        for (var i = 0; i < count; i++)
            text[i] = BitConverter.ToChar(scratch, i * 2);

        return new string(text, 0, text[count - 1] == '\0' ? count - 1 : count);
    }

    private bool DiscardBufferIfFocusChanged(int pidFallback = -1)
    {
        var focus = MacTypingFocusWatcher.ReadFocus(out var isTransient);
        if (focus.IsEmpty && pidFallback > 0)
            focus = new MacTypingFocus(pidFallback, 0);

        if (focus.Pid > 0)
            _lastForegroundPid = focus.Pid;

        return ApplyFocusSnapshot(focus, isTransient);
    }
}
