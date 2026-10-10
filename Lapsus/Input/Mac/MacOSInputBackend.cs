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
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal sealed class MacOSInputBackend : InputBackendBase<MacTypingFocus>
{

    private static readonly TimeSpan TapThreadJoin = TimeSpan.FromSeconds(2);

    private const long RestartBackoffMs = 10_000;

    private readonly MacOSNativeMethods.EventTapCallback _callback;

    private GCHandle _callbackHandle;

    private IntPtr _eventTap;
    private IntPtr _runLoop;
    private Thread? _tapThread;
    private Exception? _startError;
    private readonly ManualResetEventSlim _tapReady = new(false);
    private int _lastForegroundPid = -1;

    // Bumped on every capture stop so a key posted by the tap cannot land in a later session.
    private int _tapEpoch;

    // UI thread publishes which pid it has classified; the tap only reads this and fails open.
    // Packed so the pid and the excluded flag are published together: high 32 bits are the pid.
    private long _excludedSnapshot;

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

    private long _lastWatchdogTick;

    private long _nextRestartAt;

    public MacOSInputBackend(IPhraseCorrector corrector, AppExclusions? excludedApps = null)
        : base(corrector, excludedApps)
    {
        HotkeyVirtualKey = HotkeyTriggers.DoubleShift;
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
                Dispatcher.UIThread.Post(() =>
                {
                    if (_wantCapture)
                        MacActivity.Begin();
                });
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
        MacActivity.End();
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
        // Before capturing goes false, so a callback that already passed IsRunning still posts the old epoch.
        Interlocked.Increment(ref _tapEpoch);

        var tap = Volatile.Read(ref _eventTap);
        if (tap != IntPtr.Zero)
            MacOSNativeMethods.CGEventTapEnable(tap, false);

        Volatile.Write(ref _capturing, false);

        var runLoop = Volatile.Read(ref _runLoop);
        if (runLoop != IntPtr.Zero)
            MacOSNativeMethods.CFRunLoopStop(runLoop);
    }

    private int _watchdogPeriodMs = CaptureWatchdogPolicy.PeriodMs;

    private void ArmWatchdog()
    {
        ArmWatchdog(CaptureWatchdogPolicy.PeriodMs);
    }

    private void ArmWatchdog(int periodMs)
    {
        Volatile.Write(ref _watchdogPeriodMs, periodMs);
        Volatile.Write(ref _lastWatchdogTick, Environment.TickCount64);
        _watchdog.Change(periodMs, periodMs);
    }

    // Only retune while the user still wants capture. The lock is the same one Stop holds across
    // Disarm, so a tick already in flight cannot Change() the timer back on after capture stops.
    private void RetuneWatchdog(bool running, bool permissionsGranted)
    {
        var period = CaptureWatchdogPolicy.NextPeriodMs(running, permissionsGranted);
        lock (_lifecycle)
        {
            if (!_wantCapture || Volatile.Read(ref _watchdogPeriodMs) == period)
                return;

            Volatile.Write(ref _watchdogPeriodMs, period);
            _watchdog.Change(period, period);
        }
    }

    private void DisarmWatchdog()
    {
        _watchdog.Change(Timeout.Infinite, Timeout.Infinite);
    }

    // Runs on a timer thread. The decision is in CaptureWatchdogPolicy; this method only observes and acts.
    private void CheckPermissions()
    {
        var wanted = _wantCapture;
        var now = Environment.TickCount64;
        var gap = now - Interlocked.Exchange(ref _lastWatchdogTick, now);
        var granted = MacAccessibility.IsProcessTrusted() && MacAccessibility.IsInputMonitoringGranted();

        var running = IsRunning;
        var action = CaptureWatchdogPolicy.Decide(new WatchdogObservation(
            WantCapture: wanted,
            Running: running,
            PermissionsGranted: granted,
            GapMs: gap,
            NowMs: now,
            RestartNotBeforeMs: Volatile.Read(ref _nextRestartAt),
            RestartInFlight: Volatile.Read(ref _restarting) != 0,
            ArmedPeriodMs: Volatile.Read(ref _watchdogPeriodMs)));

        switch (action)
        {
            case WatchdogAction.StopForSleep:
                CaptureLog.Write($"Watchdog gap of {gap} ms (sleep or wake); restarting capture.");
                StopCapture();
                running = false;
                break;

            case WatchdogAction.StopForRevoke:
                CaptureLog.Write("Permission revoked while capturing; capture stopped until it is granted again.");
                StopCapture();
                running = false;
                Dispatcher.UIThread.Post(() => RaiseDiagnostic(Localizer.Instance["Status_AccessibilityRequired"]));
                break;

            case WatchdogAction.Restart:
                RestartInBackground();
                break;
        }

        if (_wantCapture)
            RetuneWatchdog(running, granted);
    }

    private void RestartInBackground()
    {
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
        Interlocked.Exchange(ref _lastForegroundPid, -1);
        Interlocked.Exchange(ref _lastEventSourcePid, 0);
        Volatile.Write(ref _excludedSnapshot, 0);
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
        var replacement = MacTextInjection.ReplaceTrailing(backspaces, text);
        switch (replacement.Result)
        {
            case MacTextInjection.AxReplace.Replaced:
                return true;
            // The trailing characters are already highlighted. Backspacing would delete that whole
            // run on the first key. A paste or a Unicode event replaces the highlight.
            case MacTextInjection.AxReplace.Selected:
                if (replacement.Engine != MacCaretProbe.WebEngineKind.None)
                    return TryPasteOverTrailing(0, text);

                if (MacTextInjection.TypeOver(0, text))
                    return true;

                RaiseDiagnosticFormat("Diag_InjectFailed", ForegroundAppName());
                return false;
            default:
                if (replacement.Engine != MacCaretProbe.WebEngineKind.None)
                    return TryPasteOverTrailing(backspaces, text);

                if (MacTextInjection.TypeOver(backspaces, text))
                    return true;

                RaiseDiagnosticFormat("Diag_InjectFailed", ForegroundAppName());
                return false;
        }
    }

    private bool TryPasteOverTrailing(int count, string text)
    {
        if (!_clipboardDirty)
            _savedClipboard = MacClipboard.CaptureSnapshot();

        // Deletes and Cmd+V are one HID sequence. A Delete posted to the pid is dropped, and the
        // paste still inserts, which leaves the old word in front of the correction.
        if (count > 0 && !MacTextInjection.SendBackspaces(count))
        {
            RaiseDiagnosticFormat("Diag_InjectFailed", ForegroundAppName());
            return false;
        }

        if (!PasteInto(0, text, onSession: true))
        {
            if (_clipboardDirty)
                _ = RestoreClipboardAsync();
            return false;
        }

        _ = RestoreClipboardAsync();
        return true;
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
        return ProcessName(ResolveForegroundPid());
    }

    private string ProcessName(int pid)
    {
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
        var known = Volatile.Read(ref _lastForegroundPid);
        if (known > 0)
            return known;

        var pid = MacTypingFocusWatcher.ReadFocus(out _).Pid;
        if (pid > 0)
            Interlocked.Exchange(ref _lastForegroundPid, pid);

        return pid;
    }

    private void RememberExclusion(int pid)
    {
        if (pid <= 0)
            return;

        var name = ProcessName(pid);
        if (name == "?")
            return;

        var packed = ((long)pid << 32) | (ExcludedApps.Contains(name) ? 1L : 0L);
        Volatile.Write(ref _excludedSnapshot, packed);
    }

    private bool ShouldSwallowHotkey(int eventPid)
    {
        // With nothing excluded there is nothing to classify: do not make the hotkey wait on a
        // pid snapshot that only exists to answer "is this app excluded".
        if (ExcludedApps.IsEmpty)
            return true;

        var packed = Volatile.Read(ref _excludedSnapshot);
        var snapshotPid = (int)(packed >> 32);
        var excluded = (packed & 1L) != 0;
        return TapUiGate.SwallowHotkey(eventPid, snapshotPid, excluded);
    }

    protected override string? ForegroundProcessName()
    {
        var name = ForegroundAppName();
        return name == "?" ? null : name;
    }

    protected override string? PlatformInputBlockedReason()
    {
        if (SkipPasswordFields && IsSecureInputEnabled())
            return Localizer.Instance["Diag_SecureField"];

        if (MacInstalledLayouts.CurrentSourceIsInputMethod())
            return Localizer.Instance["Diag_ImeActive"];

        return null;
    }

    private const int SecureInputCacheMs = 200;

    private long _secureInputCheckedAt;

    private bool _secureInputEnabled;

    // UI thread only. Secure Event Input flips when focus enters or leaves a password field, so a
    // short cache is enough and the tap does not ask the system on every key.
    private bool IsSecureInputEnabled()
    {
        var now = Environment.TickCount64;
        if (now - _secureInputCheckedAt < SecureInputCacheMs)
            return _secureInputEnabled;

        _secureInputEnabled = MacOSNativeMethods.IsSecureEventInputEnabled();
        _secureInputCheckedAt = now;
        return _secureInputEnabled;
    }

    protected override string? TypingBlockedReason()
    {
        if (DiscardBufferIfFocusChanged(Volatile.Read(ref _lastEventSourcePid)))
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

    private int _clipboardEpoch;

    protected override async Task<string?> CopySelectionAsync()
    {
        // A correction may already be holding the user's clipboard for a delayed restore.
        _clipboardEpoch++;
        if (!_clipboardDirty)
            _savedClipboard = MacClipboard.CaptureSnapshot();

        MacClipboard.WasModified();
        if (!MacTextInjection.SendCommandChord(MacOSNativeMethods.CKeyCode, MacFrontmost.ProcessId()))
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
        return PasteInto(MacFrontmost.ProcessId(), text);
    }

    private bool PasteInto(int pid, string text, bool onSession = false)
    {
        if (!MacClipboard.SetText(text))
        {
            RaiseDiagnosticFormat("Diag_ClipboardFailed", ForegroundAppName());
            return false;
        }

        // Swallow our own write so that only a later, foreign change counts when restoring.
        MacClipboard.WasModified();
        _clipboardDirty = true;
        _clipboardEpoch++;
        var pasted = onSession
            ? MacTextInjection.SendSessionCommandChord(MacOSNativeMethods.VKeyCode)
            : MacTextInjection.SendCommandChord(MacOSNativeMethods.VKeyCode, pid);
        if (pasted)
            return true;

        RaiseDiagnosticFormat("Diag_ClipboardFailed", ForegroundAppName());
        return false;
    }

    protected override async Task RestoreClipboardAsync()
    {
        if (!_clipboardDirty)
            return;

        var epoch = _clipboardEpoch;
        var saved = _savedClipboard;
        await Task.Delay(RestoreDelayMs).ConfigureAwait(true);

        // A newer correction or a selection copy took over the clipboard.
        if (epoch != _clipboardEpoch || !_clipboardDirty)
            return;

        _clipboardDirty = false;
        _savedClipboard = null;

        // The user copied something while we waited; that copy is newer than our snapshot.
        if (MacClipboard.WasModified())
        {
            CaptureLog.Write("Clipboard changed during a correction; the newer copy is kept.");
            return;
        }

        MacClipboard.RestoreSnapshot(saved);
        MacClipboard.WasModified();
    }

    private readonly Dictionary<int, string?> _executableByPid = new();

    protected override bool IsSameContainer(MacTypingFocus owner, MacTypingFocus focus)
    {
        if (owner.Pid == focus.Pid)
            return true;

        var ownerPath = ExecutablePath(owner.Pid);
        var focusPath = ExecutablePath(focus.Pid);
        if (MacAppIdentity.SameBundle(ownerPath, focusPath))
            return true;

        var front = MacFrontmost.ProcessId();
        if (front <= 0)
            return false;

        // Safari's page lives in a WebContent process whose path is not inside Safari.app.
        return (MacAppIdentity.IsWebContent(ownerPath) && focus.Pid == front)
               || (MacAppIdentity.IsWebContent(focusPath) && owner.Pid == front);
    }

    private string? ExecutablePath(int pid)
    {
        if (_executableByPid.TryGetValue(pid, out var path))
            return path;

        path = MacProcessNames.ExecutablePath(pid);
        if (_executableByPid.Count > 64)
            _executableByPid.Clear();

        _executableByPid[pid] = path;
        return path;
    }

    protected override bool RetainBufferOnUnreadableFocus =>
        BufferOwner.Pid > 0 && ChromiumApps.IsChromiumExecutable(ExecutablePath(BufferOwner.Pid));

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
        var epoch = Volatile.Read(ref _tapEpoch);
        PostFromTap(epoch, () =>
        {
            DiscardBufferIfFocusChanged();
            RememberExclusion(Volatile.Read(ref _lastForegroundPid));
        });
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

    // Tap thread: swallow only. Shared buffer state is posted to the UI thread with the epoch captured
    // at the start of this call, so a stop that lands mid-callback cannot apply the key to a new session.
    private IntPtr EventTapHandlerCore(IntPtr proxy, int type, IntPtr @event, IntPtr userInfo)
    {
        var epoch = Volatile.Read(ref _tapEpoch);

        if (type is MacOSNativeMethods.EventTapDisabledByTimeout
            or MacOSNativeMethods.EventTapDisabledByUserInput)
        {
            var tap = Volatile.Read(ref _eventTap);
            if (tap != IntPtr.Zero && IsRunning)
            {
                MacOSNativeMethods.CGEventTapEnable(tap, true);
                var reason = type;
                _ = Task.Run(() => CaptureLog.Write($"Event tap disabled by the system (reason {reason}); re-enabled."));
                PostFromTap(epoch, () => RaiseDiagnostic(Localizer.Instance["Diag_TapReenabled"]));
            }

            return @event;
        }

        if (!IsRunning)
            return PassThrough(@event);

        if (type is MacOSNativeMethods.EventLeftMouseDown
            or MacOSNativeMethods.EventRightMouseDown
            or MacOSNativeMethods.EventOtherMouseDown)
        {
            PostFromTap(epoch, () =>
            {
                NoteNonModifierInput();
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
        {
            // The target app must see a normal key. WebKit and Chromium drop an event that still
            // carries our marker, and the marker is what keeps the key out of the typing buffer.
            MacOSNativeMethods.CGEventSetIntegerValueField(
                @event, MacOSNativeMethods.EventSourceUserData, 0);
            return @event;
        }

        var keyCode = (ushort)MacOSNativeMethods.CGEventGetIntegerValueField(
            @event, MacOSNativeMethods.EventKeyboardKeycode);
        var flags = MacOSNativeMethods.CGEventGetFlags(@event);

        if (IsCapturingHotkey
            && FeedHotkeyCapture(keyCode, type == MacOSNativeMethods.EventKeyDown, KeyKindOf(keyCode), HeldModifiersOf(flags)))
            return IntPtr.Zero;

        var pid = NoteEventProcess(@event);

        if (type == MacOSNativeMethods.EventFlagsChanged)
        {
            if (ModifierOf(keyCode) is { } modifier)
            {
                var down = (flags & FlagMaskOf(modifier)) != 0;
                PostFromTap(epoch, () => NoteModifierKey(modifier, down, defer: false));
            }

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
                PostFromTap(epoch, run);
            return IntPtr.Zero;
        }

        if (IsCorrectionHotkey(keyCode, flags))
        {
            PostFromTap(epoch, NoteNonModifierInput);
            if (!ShouldSwallowHotkey(pid))
                return @event;

            _swallowedKeyDown = keyCode;
            _pendingHotkeyAction = ApplyCorrection;
            return IntPtr.Zero;
        }

        if (SelectionActionForKey(keyCode, HeldModifiersOf(flags), !HasMeaningfulModifiers(flags)) is { } action)
        {
            PostFromTap(epoch, NoteNonModifierInput);
            if (!ShouldSwallowHotkey(pid))
                return @event;

            _swallowedKeyDown = keyCode;
            _pendingHotkeyAction = () => _ = ApplySelectionActionAsync(action);
            return IntPtr.Zero;
        }

        var typed = TypedText(@event);
        var autoRepeat = MacOSNativeMethods.CGEventGetIntegerValueField(
            @event, MacOSNativeMethods.EventKeyboardAutorepeat) != 0;
        PostFromTap(epoch, () =>
        {
            NoteNonModifierInput();
            CaptureKey(keyCode, flags, autoRepeat, typed, pid);
        });
        return @event;
    }

    private void PostFromTap(int epoch, Action action)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!TapUiGate.Accept(epoch, Volatile.Read(ref _tapEpoch), IsRunning))
                return;

            action();
        });
    }

    // Capture is off, but an in-flight injection must still lose its marker or Chromium drops the key.
    private static IntPtr PassThrough(IntPtr @event)
    {
        if (@event != IntPtr.Zero
            && MacOSNativeMethods.CGEventGetIntegerValueField(@event, MacOSNativeMethods.EventSourceUserData)
                == MacOSNativeMethods.InjectedMarker)
        {
            MacOSNativeMethods.CGEventSetIntegerValueField(
                @event, MacOSNativeMethods.EventSourceUserData, 0);
        }

        return @event;
    }

    private int _lastEventSourcePid;

    private int NoteEventProcess(IntPtr @event)
    {
        var pid = (int)MacOSNativeMethods.CGEventGetIntegerValueField(
            @event, MacOSNativeMethods.EventSourceUnixProcessId);

        Interlocked.Exchange(ref _lastEventSourcePid, pid);
        if (pid > 0)
            Interlocked.Exchange(ref _lastForegroundPid, pid);

        return pid;
    }

    private bool IsCorrectionHotkey(ushort keyCode, ulong flags)
    {
        if (HotkeyVirtualKey == MacHotkeys.CtrlOptionSpace)
            return keyCode == MacOSNativeMethods.SpaceKeyCode && HasCtrlOption(flags);

        return TriggerMatches(HotkeyVirtualKey, keyCode, HeldModifiersOf(flags), !HasMeaningfulModifiers(flags));
    }

    private static HotkeyModifiers HeldModifiersOf(ulong flags)
    {
        var held = HotkeyModifiers.None;
        if ((flags & MacOSNativeMethods.EventFlagMaskShift) != 0)
            held |= HotkeyModifiers.Shift;
        if ((flags & MacOSNativeMethods.EventFlagMaskControl) != 0)
            held |= HotkeyModifiers.Control;
        if ((flags & MacOSNativeMethods.EventFlagMaskAlternate) != 0)
            held |= HotkeyModifiers.Alt;
        if ((flags & MacOSNativeMethods.EventFlagMaskCommand) != 0)
            held |= HotkeyModifiers.Meta;

        return held;
    }

    private static HotkeyKeyKind KeyKindOf(ushort keyCode)
    {
        if (ModifierOf(keyCode) is not null)
            return HotkeyKeyKind.Modifier;

        if (keyCode == MacOSNativeMethods.EscapeKeyCode)
            return HotkeyKeyKind.Escape;

        // Caps Lock, Help/Insert, Home, Page Up/Down, Forward Delete, End, and F1-F20 (ANSI keycodes).
        var named = keyCode is 0x39 or 0x72 or 0x73 or 0x74 or 0x75 or 0x77 or 0x79
            or 0x7A or 0x78 or 0x63 or 0x76 or 0x60 or 0x61 or 0x62 or 0x64 or 0x65 or 0x6D or 0x67 or 0x6F
            or 0x69 or 0x6B or 0x71 or 0x6A or 0x40 or 0x4F or 0x50 or 0x5A;
        return named ? HotkeyKeyKind.Named : HotkeyKeyKind.Typing;
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

        RememberExclusion(pid);

        DropTypingBufferIfIdle();
        EndCorrectionCycle();

        if (IsExcludedApp(out _))
        {
            ResetTypingBuffer();
            return;
        }

        // Skip when Secure Event Input is on (password fields / Terminal); tap sees no keys.
        if (SkipPasswordFields && IsSecureInputEnabled())
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
            Interlocked.Exchange(ref _lastForegroundPid, focus.Pid);

        return ApplyFocusSnapshot(focus, isTransient);
    }
}
