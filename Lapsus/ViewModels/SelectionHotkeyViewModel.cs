using CommunityToolkit.Mvvm.ComponentModel;
using Lapsus.Core.Text;
using Lapsus.Localization;
using System;
using System.Collections.Generic;

namespace Lapsus.ViewModels;

public sealed partial class SelectionHotkeyViewModel : ViewModelBase
{
    private readonly Action<SelectionHotkeyViewModel> _changed;
    private readonly string _labelKey;
    private readonly string _hintKey;
    private bool _suppressChanged;

    public SelectionHotkeyViewModel(
        SelectionAction action, string labelKey, string hintKey,
        IReadOnlyList<HotkeyOption> options, HotkeyOption selected,
        Action<SelectionHotkeyViewModel> changed, HotkeyCapture capture, Func<int, HotkeyOption> recordedOption)
    {
        Action = action;
        Options = options;
        _labelKey = labelKey;
        _hintKey = hintKey;
        _changed = changed;
        _selected = selected;
        Recorder = new HotkeyRecorderViewModel(capture, trigger => Selected = recordedOption(trigger));
    }

    public SelectionAction Action { get; }

    public IReadOnlyList<HotkeyOption> Options { get; }

    public HotkeyRecorderViewModel Recorder { get; }

    public string Label => Localizer.Instance[_labelKey];

    public string Hint => Localizer.Instance[_hintKey];

    [ObservableProperty] private HotkeyOption _selected;

    public HotkeyOption Off => Options[0];

    public void SetSilently(HotkeyOption option)
    {
        _suppressChanged = true;
        try
        {
            Selected = option;
        }
        finally
        {
            _suppressChanged = false;
        }
    }

    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Hint));
    }

    partial void OnSelectedChanged(HotkeyOption value)
    {
        if (!_suppressChanged)
            _changed(this);
    }
}
