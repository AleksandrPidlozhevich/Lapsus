using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Core.Input;
using Lapsus.Input;
using Lapsus.Localization;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Lapsus.ViewModels;

// The Record button of one hotkey row. While recording, the backend hands the next key press here instead of the app.
public sealed partial class HotkeyRecorderViewModel : ViewModelBase
{
    private readonly HotkeyCapture _capture;
    private readonly Action<int> _recorded;

    public HotkeyRecorderViewModel(HotkeyCapture capture, Action<int> recorded)
    {
        _capture = capture;
        _recorded = recorded;
    }

    // Chips for the modifiers held right now.
    public ObservableCollection<string> HeldKeys { get; } = [];

    [ObservableProperty] private bool _isRecording;

    [ObservableProperty] private bool _isRejected;

    public string RecordLabel => Localizer.Instance["HotkeyRecord_Button"];

    public string CancelLabel => Localizer.Instance["HotkeyRecord_Cancel"];

    public string HintText => Localizer.Instance["HotkeyRecord_Hint"];

    public string PromptText => Localizer.Instance[IsRejected ? "HotkeyRecord_NeedModifier" : "HotkeyRecord_Prompt"];

    partial void OnIsRejectedChanged(bool value)
    {
        OnPropertyChanged(nameof(PromptText));
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRecording)
            return;

        IsRecording = true;
        var started = await _capture.BeginAsync(OnCapture, Stop).ConfigureAwait(true);
        if (started && IsRecording)
            return;

        // Cancelled while the backend was still starting: the capture must not outlive the cancel.
        if (started)
            _capture.End();

        IsRecording = false;
    }

    [RelayCommand]
    private void Cancel()
    {
        Stop();
    }

    public void Stop()
    {
        if (!IsRecording)
            return;

        IsRecording = false;
        IsRejected = false;
        HeldKeys.Clear();
        _capture.End();
    }

    private void OnCapture(HotkeyCaptureEvent evt)
    {
        if (!IsRecording)
            return;

        switch (evt.Kind)
        {
            case HotkeyCaptureKind.Held:
                IsRejected = false;
                HeldKeys.Clear();
                foreach (var label in HotkeyNames.ModifierLabels(evt.Held))
                    HeldKeys.Add(label);
                break;

            case HotkeyCaptureKind.Rejected:
                IsRejected = true;
                break;

            case HotkeyCaptureKind.Captured:
                Stop();
                _recorded(evt.Trigger);
                break;

            case HotkeyCaptureKind.Cancelled:
                Stop();
                break;
        }
    }
}
