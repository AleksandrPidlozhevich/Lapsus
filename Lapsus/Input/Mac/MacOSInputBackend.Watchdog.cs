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

    // Runs on a timer thread.
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
}
