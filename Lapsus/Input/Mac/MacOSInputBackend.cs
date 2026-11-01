using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Localization;
using Lapsus.Startup;
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
internal sealed partial class MacOSInputBackend : InputBackendBase<MacTypingFocus>
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

    private readonly object _lifecycle = new();

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

    protected override MacTypingFocus CurrentFocus()
    {
        var focus = MacTypingFocusWatcher.ReadFocus(out var isTransient);
        // A focus read mid-change (Electron rebuilding its tree) says nothing; the line's owner is the better guess.
        return isTransient || focus.IsEmpty ? BufferOwner : focus;
    }

    protected override bool SetClipboardText(string text) => MacClipboard.SetText(text);

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
}
