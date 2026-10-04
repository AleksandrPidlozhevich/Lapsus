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

    private const int WatchdogPeriodMs = 2000;

    private const long RestartBackoffMs = 10_000;

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

    // Set by the user's intent (Start/Stop), read by the watchdog from its own thread.
    private volatile bool _wantCapture;

    // Serializes start attempts; never held by Stop, so disabling capture never waits on a start.
    private readonly SemaphoreSlim _startGate = new(1, 1);

    // Guards _tapThread and the flags that Start and Stop change together.
    private readonly object _lifecycle = new();

    // Re-checks Accessibility and Input Monitoring while the user wants capture running.
    private readonly System.Threading.Timer _watchdog;

    private int _restarting;

    private long _nextRestartAt;

    public MacOSInputBackend(IPhraseCorrector corrector, AppExclusions? excludedApps = null)
        : base(corrector, excludedApps)
    {
        HotkeyVirtualKey = MacHotkeys.CtrlOptionSpace;
        _callback = EventTapHandler;
        _callbackHandle = GCHandle.Alloc(_callback);
        _watchdog = new System.Threading.Timer(_ => CheckPermissions(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public override bool IsRunning => Volatile.Read(ref _capturing);

    public override bool SkipPasswordFields { get; set; } = true;

    // Called by the UI on a worker thread (see SettingsViewModel.ApplyEnabled): creating the tap
    // can take seconds when the OS is slow to answer, and must not freeze the settings window.
    public override void Start()
    {
        StartCapture(userInitiated: true);
    }

    // Returns true when this call started capture, false when it was already running or not wanted.
    private bool StartCapture(bool userInitiated)
    {
        _startGate.Wait();
        try
        {
            lock (_lifecycle)
            {
                if (userInitiated)
                    _wantCapture = true;

                if (!_wantCapture)
                    return false;

                if (IsRunning)
                {
                    ArmWatchdog();
                    return false;
                }
            }

            // A previous tap thread may still be winding down after a stop or a watchdog stop.
            var previous = CurrentTapThread();
            if (previous is { IsAlive: true } && !previous.Join(TapThreadJoin))
                throw new InvalidOperationException("The previous keyboard capture thread did not exit in time.");

            lock (_lifecycle)
            {
                _tapReady.Reset();
                _startError = null;
                _tapThread = new Thread(RunTapThread)
                {
                    IsBackground = true,
                    Name = "Lapsus CGEventTap"
                };
                _tapThread.Start();
            }

            CaptureLog.Write(userInitiated ? "Capture start requested." : "Capture restart requested.");

            if (!_tapReady.Wait(TimeSpan.FromSeconds(3)))
            {
                StopCapture();
                throw new InvalidOperationException(Localizer.Instance["Status_AccessibilityRequired"]);
            }

            lock (_lifecycle)
            {
                if (_startError is { } error)
                    throw error;

                if (Volatile.Read(ref _eventTap) == IntPtr.Zero)
                    throw new InvalidOperationException(Localizer.Instance["Status_AccessibilityRequired"]);

                // A stop may have arrived while the tap was being created.
                if (!_wantCapture)
                {
                    StopCapture();
                    return false;
                }

                Volatile.Write(ref _capturing, true);
                ArmWatchdog();
                CaptureLog.Write("Capture running.");
                return true;
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    public override void Stop()
    {
        Thread? thread;
        lock (_lifecycle)
        {
            _wantCapture = false;
            DisarmWatchdog();
            StopCapture();
            thread = _tapThread;
        }

        thread?.Join(TapThreadJoin);

        lock (_lifecycle)
        {
            if (thread is { IsAlive: false } && ReferenceEquals(_tapThread, thread))
                _tapThread = null;
        }

        _swallowedKeyDown = null;
        _pendingHotkeyAction = null;
        ForgetTypingContext();
        CaptureLog.Write("Capture stopped.");
    }

    public override void Dispose()
    {
        Stop();
        _watchdog.Dispose();

        // The native callback may still run while its thread lives; only release the handle after that.
        if (_callbackHandle.IsAllocated && CurrentTapThread() is not { IsAlive: true })
            _callbackHandle.Free();
    }

    private Thread? CurrentTapThread()
    {
        lock (_lifecycle)
            return _tapThread;
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

    private void ArmWatchdog()
    {
        _watchdog.Change(WatchdogPeriodMs, WatchdogPeriodMs);
    }

    private void DisarmWatchdog()
    {
        _watchdog.Change(Timeout.Infinite, Timeout.Infinite);
    }

    // Runs on a timer thread. Stops capture when a permission disappears and restarts it when the
    // user grants it again, so the app does not need a relaunch after the permission dialog.
    private void CheckPermissions()
    {
        if (!_wantCapture)
            return;

        var granted = MacAccessibility.IsProcessTrusted() && MacAccessibility.IsInputMonitoringGranted();

        if (IsRunning)
        {
            if (granted)
                return;

            CaptureLog.Write("Permission revoked while capturing; capture stopped until it is granted again.");
            StopCapture();
            Dispatcher.UIThread.Post(() => RaiseDiagnostic(Localizer.Instance["Status_AccessibilityRequired"]));
            return;
        }

        if (!granted || Environment.TickCount64 < Volatile.Read(ref _nextRestartAt))
            return;

        if (Interlocked.Exchange(ref _restarting, 1) != 0)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                if (StartCapture(userInitiated: false))
                    Dispatcher.UIThread.Post(() => RaiseDiagnostic(Localizer.Instance["Diag_CaptureRestored"]));
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _nextRestartAt, Environment.TickCount64 + RestartBackoffMs);
                CaptureLog.Write($"Restart after permission change failed: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _restarting, 0);
            }
        });
    }

    private void ForgetTypingContext()
    {
        ClearTypingContext(default);
        _deadKeyState = 0;
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
        {
            _deadKeyState = 0;
            return Localizer.Instance.Format("Diag_FocusChanged", ForegroundAppName());
        }

        // The app still holds the accent as marked text; no injection path is known to replace around it safely.
        if (_deadKeyState != 0)
            return Localizer.Instance["Diag_DeadKeyPending"];

        return InputBlockedReason();
    }

    private const int CopyTimeoutMs = 500;

    private const int CopyPollMs = 10;

    private const int ReadAttempts = 3;

    private const int RestoreDelayMs = 500;

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

        // Swallow our own write so that only a later, foreign change counts when restoring.
        MacClipboard.WasModified();
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

        // The user copied something while we waited; that copy is newer than our snapshot.
        if (MacClipboard.WasModified())
        {
            CaptureLog.Write("Clipboard changed during a correction; the newer copy is kept.");
            return;
        }

        MacClipboard.RestoreSnapshot(saved);
        MacClipboard.WasModified();
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

            CaptureLog.Write(
                $"Event tap could not be created (accessibility={MacAccessibility.IsProcessTrusted()}, " +
                $"input monitoring={MacAccessibility.IsInputMonitoringGranted()}).");
            _startError = new InvalidOperationException(Localizer.Instance["Status_AccessibilityRequired"]);
            _tapReady.Set();
            return;
        }

        Volatile.Write(ref _eventTap, tap);

        var source = MacOSNativeMethods.CFMachPortCreateRunLoopSource(IntPtr.Zero, tap, 0);
        MacOSNativeMethods.CFRunLoopAddSource(runLoop, source, MacOSNativeMethods.RunLoopDefaultMode);
        MacOSNativeMethods.CGEventTapEnable(tap, true);

        var watcher = new MacTypingFocusWatcher(OnAccessibilityFocusChanged);
        if (!watcher.TryStart(runLoop))
            CaptureLog.Write("Focus observer did not start; focus changes are detected late.");

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
                var reason = type;
                _ = Task.Run(() => CaptureLog.Write($"Event tap disabled by the system (reason {reason}); re-enabled."));
                Dispatcher.UIThread.Post(() => RaiseDiagnostic(Localizer.Instance["Diag_TapReenabled"]));
            }

            return @event;
        }

        if (type is MacOSNativeMethods.EventLeftMouseDown
            or MacOSNativeMethods.EventRightMouseDown
            or MacOSNativeMethods.EventOtherMouseDown)
        {
            NoteNonModifierInput();
            Dispatcher.UIThread.Post(() =>
            {
                _deadKeyState = 0;
                ResetTypingBuffer();
            });
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
        var autoRepeat = MacOSNativeMethods.CGEventGetIntegerValueField(
            @event, MacOSNativeMethods.EventKeyboardAutorepeat) != 0;
        Dispatcher.UIThread.Post(() => CaptureKey(keyCode, flags, autoRepeat, typed, pid));
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

    private void CaptureKey(ushort keyCode, ulong flags, bool autoRepeat, string typed, int pid)
    {
        // Only a plain keystroke on the same layout may complete a dead key; every other path drops it.
        var pendingDeadKey = _deadKeyState;
        _deadKeyState = 0;

        if (DiscardBufferIfFocusChanged(pid))
            pendingDeadKey = 0;

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
        if (layoutToken != _deadKeyLayout)
            pendingDeadKey = 0;

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
            if (pendingDeadKey != 0 && !HasMeaningfulModifiers(flags))
                return;

            BackspaceTypingBuffer(HasMeaningfulModifiers(flags));
            return;
        }

        if (HasMeaningfulModifiers(flags))
        {
            ResetTypingBuffer();
            return;
        }

        NoteKeystrokeLayout(layoutToken);

        // The event's own string knows nothing of a pending dead key: after ; on Greek it reports α, not ά.
        var state = pendingDeadKey;
        if (MacInstalledLayouts.TranslateKeyDown(keyCode, flags, autoRepeat, ref state, _translated, out var length))
        {
            if (length == 0 && state != 0)
            {
                _deadKeyState = state;
                _deadKeyLayout = layoutToken;
                return;
            }

            if (pendingDeadKey != 0)
            {
                AppendCapturedText(_translated.AsSpan(0, length));
                return;
            }
        }

        AppendCapturedText(typed);
    }

    private uint _deadKeyState;
    private long _deadKeyLayout;
    private readonly char[] _translated = new char[4];

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
