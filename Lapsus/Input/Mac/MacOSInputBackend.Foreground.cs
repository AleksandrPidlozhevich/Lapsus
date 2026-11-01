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

internal sealed partial class MacOSInputBackend
{
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

    protected override string? PlatformInputBlockedReason(bool passwords)
    {
        if (passwords && SkipPasswordFields && IsSecureInputEnabled())
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
}
