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

// The CGEventTap thread and what it does with each key event.
internal sealed partial class MacOSInputBackend
{
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
        if (!watcher.TryStart(runLoop) && !MacSandbox.IsSandboxed)
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
            && FeedHotkeyCapture(keyCode, type == MacOSNativeMethods.EventKeyDown, MacHotkeys.KeyKindOf(keyCode), MacHotkeys.HeldModifiersOf(flags)))
            return IntPtr.Zero;

        var pid = NoteEventProcess(@event);

        if (type == MacOSNativeMethods.EventFlagsChanged)
        {
            var held = MacHotkeys.HeldModifiersOf(flags);
            PostFromTap(epoch, () => NoteDictationModifiers(held));
            if (MacHotkeys.ModifierOf(keyCode) is { } modifier)
            {
                var down = (flags & MacHotkeys.FlagMaskOf(modifier)) != 0;
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

        // A held hotkey repeats its key-down; it already did its work on the first one.
        if (_swallowedKeyDown == keyCode)
            return IntPtr.Zero;

        PostFromTap(epoch, NoteDictationOtherKey);

        // Esc during a recording cancels it and goes no further: the app behind must not see it.
        if (keyCode == MacOSNativeMethods.EscapeKeyCode && DictationListening)
        {
            _swallowedKeyDown = keyCode;
            _pendingHotkeyAction = RaiseDictationCancel;
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

        if (SelectionActionForKey(keyCode, MacHotkeys.HeldModifiersOf(flags), !MacHotkeys.HasMeaningfulModifiers(flags)) is { } action)
        {
            PostFromTap(epoch, NoteNonModifierInput);
            if (!ShouldSwallowHotkey(pid))
                return @event;

            _swallowedKeyDown = keyCode;
            _pendingHotkeyAction = () => _ = ApplySelectionActionAsync(action);
            return IntPtr.Zero;
        }

        if (TriggerMatches(DictationHotkey, keyCode, MacHotkeys.HeldModifiersOf(flags), !MacHotkeys.HasMeaningfulModifiers(flags)))
        {
            PostFromTap(epoch, NoteNonModifierInput);
            if (!ShouldSwallowHotkey(pid))
                return @event;

            // Starts on the key going down, so it can be held while speaking; letting go is the release.
            _swallowedKeyDown = keyCode;
            PostFromTap(epoch, RaiseDictationHotkey);
            _pendingHotkeyAction = () => RaiseDictationReleased();
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
            return keyCode == MacOSNativeMethods.SpaceKeyCode && MacHotkeys.HasCtrlOption(flags);

        return TriggerMatches(HotkeyVirtualKey, keyCode, MacHotkeys.HeldModifiersOf(flags), !MacHotkeys.HasMeaningfulModifiers(flags));
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

        if (MacHotkeys.IsCaretMovingKey(keyCode))
        {
            ResetTypingBuffer();
            return;
        }

        if (keyCode == MacOSNativeMethods.DeleteKeyCode)
        {
            if (pendingDeadKey != 0 && !MacHotkeys.HasMeaningfulModifiers(flags))
                return;

            BackspaceTypingBuffer(MacHotkeys.HasMeaningfulModifiers(flags));
            return;
        }

        if (MacHotkeys.HasMeaningfulModifiers(flags))
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
