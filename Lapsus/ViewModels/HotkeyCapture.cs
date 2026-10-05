using Lapsus.Core.Input;
using Lapsus.Localization;
using System;
using System.Threading.Tasks;

namespace Lapsus.ViewModels;

// Owns the backend's shortcut capture for the settings window. Only one recorder holds it at a time, and a backend
// that was stopped (Capture off) is started just for the recording and stopped again afterwards.
public sealed class HotkeyCapture(IInputBackend backend, Func<bool> isEnabled, Action<string> reportFailure)
{
    private Action? _stopOwner;

    private bool _startedForRecording;

    public async Task<bool> BeginAsync(Action<HotkeyCaptureEvent> sink, Action stopOwner)
    {
        _stopOwner?.Invoke();

        if (!backend.IsRunning)
        {
            try
            {
                // Same threading as ApplyEnabled: Start on macOS can take seconds, Windows needs the UI thread.
                if (OperatingSystem.IsMacOS())
                    await Task.Run(() => backend.Start()).ConfigureAwait(true);
                else
                    backend.Start();
            }
            catch (Exception ex)
            {
                reportFailure(Localizer.Instance.Format("Status_StartFailed", ex.Message));
                return false;
            }

            _startedForRecording = true;
        }

        _stopOwner = stopOwner;
        backend.BeginHotkeyCapture(sink);
        return true;
    }

    public void End()
    {
        _stopOwner = null;
        backend.CancelHotkeyCapture();

        if (!_startedForRecording)
            return;

        _startedForRecording = false;
        if (!isEnabled())
            backend.Stop();
    }

    // The recording owner stops itself, which calls End.
    public void CancelActive()
    {
        _stopOwner?.Invoke();
    }
}
