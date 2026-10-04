using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal sealed class MacTypingFocusWatcher : IDisposable
{
    private readonly MacOSNativeMethods.AXObserverCallback _callback;
    private readonly Action _onFocusContextChanged;
    private GCHandle _callbackHandle;

    private IntPtr _observer;
    private IntPtr _systemWide;
    private IntPtr _runLoop;
    private IntPtr _runLoopSource;
    private bool _started;

    public MacTypingFocusWatcher(Action onFocusContextChanged)
    {
        _onFocusContextChanged = onFocusContextChanged;
        _callback = OnAxNotification;
        _callbackHandle = GCHandle.Alloc(_callback);
    }

    public bool TryStart(IntPtr runLoop)
    {
        if (_started || runLoop == IntPtr.Zero || !MacAccessibility.IsProcessTrusted())
            return false;

        _systemWide = MacOSNativeMethods.AXUIElementCreateSystemWide();
        if (_systemWide == IntPtr.Zero)
            return false;

        MacOSNativeMethods.AXUIElementSetMessagingTimeout(_systemWide, AxTimeoutSeconds);

        var create = MacOSNativeMethods.AXObserverCreate(
            MacOSNativeMethods.getpid(), _callback, out _observer);
        if (create != 0 || _observer == IntPtr.Zero)
        {
            MacOSNativeMethods.CFRelease(_systemWide);
            _systemWide = IntPtr.Zero;
            return false;
        }

        MacOSNativeMethods.AXObserverAddNotification(
            _observer, _systemWide, MacOSNativeMethods.AxFocusedWindowChangedNotification, IntPtr.Zero);
        MacOSNativeMethods.AXObserverAddNotification(
            _observer, _systemWide, MacOSNativeMethods.AxFocusedUiElementChangedNotification, IntPtr.Zero);

        _runLoopSource = MacOSNativeMethods.AXObserverGetRunLoopSource(_observer);
        MacOSNativeMethods.CFRetain(_runLoopSource);
        MacOSNativeMethods.CFRunLoopAddSource(runLoop, _runLoopSource, MacOSNativeMethods.RunLoopDefaultMode);
        _runLoop = runLoop;
        _started = true;
        return true;
    }

    public void Dispose()
    {
        if (_started && _observer != IntPtr.Zero && _systemWide != IntPtr.Zero)
        {
            MacOSNativeMethods.AXObserverRemoveNotification(
                _observer, _systemWide, MacOSNativeMethods.AxFocusedWindowChangedNotification);
            MacOSNativeMethods.AXObserverRemoveNotification(
                _observer, _systemWide, MacOSNativeMethods.AxFocusedUiElementChangedNotification);
        }

        if (_runLoopSource != IntPtr.Zero)
        {

            if (_runLoop != IntPtr.Zero)
                MacOSNativeMethods.CFRunLoopRemoveSource(
                    _runLoop, _runLoopSource, MacOSNativeMethods.RunLoopDefaultMode);

            MacOSNativeMethods.CFRelease(_runLoopSource);
            _runLoopSource = IntPtr.Zero;
        }

        _runLoop = IntPtr.Zero;

        if (_observer != IntPtr.Zero)
        {
            MacOSNativeMethods.CFRelease(_observer);
            _observer = IntPtr.Zero;
        }

        if (_systemWide != IntPtr.Zero)
        {
            MacOSNativeMethods.CFRelease(_systemWide);
            _systemWide = IntPtr.Zero;
        }

        if (_callbackHandle.IsAllocated)
            _callbackHandle.Free();

        _started = false;
    }

    private void OnAxNotification(IntPtr observer, IntPtr element, IntPtr notification, IntPtr refcon)
    {

        InvalidateFocusCache();
        _onFocusContextChanged();
    }

    private const float AxTimeoutSeconds = 0.1f;

    private static readonly IntPtr SystemWideElement = CreateTimedOutSystemWideElement();

    private static IntPtr CreateTimedOutSystemWideElement()
    {
        var element = MacOSNativeMethods.AXUIElementCreateSystemWide();
        if (element != IntPtr.Zero)
            MacOSNativeMethods.AXUIElementSetMessagingTimeout(element, AxTimeoutSeconds);

        return element;
    }

    private const long FocusCacheMs = 100;

    private static readonly object FocusGate = new();
    private static MacTypingFocus _cachedFocus;
    private static bool _cachedTransient;
    private static bool _cacheValid;
    private static long _cachedAt;

    public static MacTypingFocus ReadFocus(out bool isTransient)
    {
        lock (FocusGate)
        {
            if (_cacheValid && Environment.TickCount64 - _cachedAt < FocusCacheMs)
            {
                isTransient = _cachedTransient;
                return _cachedFocus;
            }

            var focus = ReadFocusUncached(out isTransient);
            _cachedFocus = focus;
            _cachedTransient = isTransient;
            _cachedAt = Environment.TickCount64;
            _cacheValid = true;
            return focus;
        }
    }

    public static void InvalidateFocusCache()
    {
        lock (FocusGate)
        {
            _cacheValid = false;
        }
    }

    // Lock-free: safe to call from the caret sampler without delaying the event tap.
    internal static MacTypingFocus ReadFocusUncached(out bool isTransient)
    {
        isTransient = false;
        var systemWide = SystemWideElement;
        if (systemWide == IntPtr.Zero)
            return default;

        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                systemWide, MacOSNativeMethods.AxFocusedUiElementAttribute, out var focused) != 0
            || focused == IntPtr.Zero)
            return default;

        try
        {
            if (MacOSNativeMethods.AXUIElementGetPid(focused, out var pid) != 0 || pid <= 0)
                return default;

            var role = ReadRole(focused);
            isTransient = IsTransientRole(role);
            var hash = MacOSNativeMethods.CFHash(focused);
            return new MacTypingFocus(pid, hash);
        }
        finally
        {
            MacOSNativeMethods.CFRelease(focused);
        }
    }

    private static string ReadRole(IntPtr element)
    {
        if (MacOSNativeMethods.AXUIElementCopyAttributeValue(
                element, MacOSNativeMethods.AxRoleAttribute, out var role) != 0
            || role == IntPtr.Zero)
            return string.Empty;

        try
        {
            var sb = new StringBuilder(64);
            return MacOSNativeMethods.CFStringGetCString(
                role, sb, sb.Capacity, MacOSNativeMethods.CFStringEncodingUtf8)
                ? sb.ToString()
                : string.Empty;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(role);
        }
    }

    private static bool IsTransientRole(string role)
    {
        return role is "AXMenu" or "AXMenuItem" or "AXMenuBar" or "AXMenuBarItem"
            or "AXSystemDialog" or "AXTooltip";
    }
}

[SupportedOSPlatform("macos")]
internal readonly record struct MacTypingFocus(int Pid, nint ElementHash) : ITypingFocus
{
    public bool IsEmpty => Pid <= 0;
}
