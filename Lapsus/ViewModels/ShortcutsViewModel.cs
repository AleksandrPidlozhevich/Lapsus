using CommunityToolkit.Mvvm.ComponentModel;
using Lapsus.Core.Input;
using Lapsus.Core.Text;
using Lapsus.Input;
using Lapsus.Input.Dictation;
using Lapsus.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Lapsus.ViewModels;

// No two hotkeys share a key.
public sealed partial class ShortcutsViewModel : ObservableObject
{
    private readonly IInputBackend _backend;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly bool _supported;
    private readonly HotkeyCapture _capture;
    private readonly Func<bool> _paidFeatures;
    private readonly Action _dictationKeyChanged;
    private readonly Action _changed;

    public ShortcutsViewModel(
        IInputBackend backend, SettingsStore store, AppSettings settings, bool supported, HotkeyCapture capture,
        Func<bool> paidFeatures, Action dictationKeyChanged, Action changed)
    {
        _backend = backend;
        _store = store;
        _settings = settings;
        _supported = supported;
        _capture = capture;
        _paidFeatures = paidFeatures;
        _dictationKeyChanged = dictationKeyChanged;
        _changed = changed;

        _selectedHotkey = ResolveHotkey(settings.HotkeyVirtualKey);
        MainRecorder = new HotkeyRecorderViewModel(_capture, trigger => SelectedHotkey = AddRecordedOption(trigger));
        SelectionActions = BuildSelectionActions();
        DictationHotkeyRow = BuildDictationHotkeyRow();
        _backend.HotkeyVirtualKey = _selectedHotkey.VirtualKey;
    }

    private readonly ObservableCollection<HotkeyOption> _hotkeyOptions = new(BuiltInHotkeys);

    private readonly ObservableCollection<HotkeyOption> _selectionKeyOptions = new(BuiltInSelectionKeys);

    private readonly ObservableCollection<HotkeyOption> _dictationKeyOptions = new(BuiltInSelectionKeys);

    public IReadOnlyList<HotkeyOption> Hotkeys => _hotkeyOptions;

    private static IReadOnlyList<HotkeyOption> BuiltInHotkeys =>
        OperatingSystem.IsMacOS()
            ?
            [
                // ⌃⌥Space is macOS symbolic hotkey 61, "select the next input source". It cycles
                // layouts on its own, so the correction never gets to replace the typed word.
                new HotkeyOption("⇧ ×2", HotkeyTriggers.DoubleShift),
                new HotkeyOption("⌃ ×2", HotkeyTriggers.DoubleControl),
                new HotkeyOption("⌥ ×2", HotkeyTriggers.DoubleAlt),
                new HotkeyOption("⌘ ×2", HotkeyTriggers.DoubleCommand)
            ]
            :
            [
                new HotkeyOption("Pause / Break", 0x13),
                new HotkeyOption("Scroll Lock", 0x91),
                new HotkeyOption("F12", 0x7B),
                new HotkeyOption("F11", 0x7A),
                new HotkeyOption("Insert", 0x2D),
                new HotkeyOption("Caps Lock", 0x14),
                new HotkeyOption("Shift ×2", HotkeyTriggers.DoubleShift),
                new HotkeyOption("Ctrl ×2", HotkeyTriggers.DoubleControl),
                new HotkeyOption("Alt ×2", HotkeyTriggers.DoubleAlt)
            ];

    private static IReadOnlyList<HotkeyOption> BuiltInSelectionKeys =>
        OperatingSystem.IsMacOS()
            ?
            [
                new HotkeyOption("—", 0),
                new HotkeyOption("F12", 0x6F),
                new HotkeyOption("F11", 0x67),
                new HotkeyOption("F10", 0x6D),
                new HotkeyOption("⇧ ×2", HotkeyTriggers.DoubleShift),
                new HotkeyOption("⌃ ×2", HotkeyTriggers.DoubleControl),
                new HotkeyOption("⌥ ×2", HotkeyTriggers.DoubleAlt),
                new HotkeyOption("⌘ ×2", HotkeyTriggers.DoubleCommand)
            ]
            :
            [
                new HotkeyOption("—", 0),
                new HotkeyOption("Scroll Lock", 0x91),
                new HotkeyOption("F12", 0x7B),
                new HotkeyOption("F11", 0x7A),
                new HotkeyOption("Insert", 0x2D),
                new HotkeyOption("Pause / Break", 0x13),
                new HotkeyOption("Caps Lock", 0x14),
                new HotkeyOption("Shift ×2", HotkeyTriggers.DoubleShift),
                new HotkeyOption("Ctrl ×2", HotkeyTriggers.DoubleControl),
                new HotkeyOption("Alt ×2", HotkeyTriggers.DoubleAlt)
            ];

    public IReadOnlyList<SelectionHotkeyViewModel> SelectionActions { get; }

    private static readonly (SelectionAction Action, string Label, string Hint)[] SelectionActionRows =
    [
        (SelectionAction.Correct, "SelectionHotkey_Label", "SelectionHotkey_Hint"),
        (SelectionAction.CycleCase, "SelectionCase_Label", "SelectionCase_Hint"),
        (SelectionAction.Transliterate, "SelectionTranslit_Label", "SelectionTranslit_Hint"),
        (SelectionAction.ReverseRtl, "SelectionRtl_Label", "SelectionRtl_Hint")
    ];

    public HotkeyRecorderViewModel MainRecorder { get; }

    private IReadOnlyList<SelectionHotkeyViewModel> BuildSelectionActions()
    {
        var options = _selectionKeyOptions;
        var rows = new List<SelectionHotkeyViewModel>(SelectionActionRows.Length);

        var taken = new HashSet<int> { SelectedHotkey.VirtualKey };
        var changed = false;

        foreach (var (action, label, hint) in SelectionActionRows)
        {
            var saved = SavedSelectionHotkey(action);
            var option = options.FirstOrDefault(o => o.VirtualKey == saved);
            if (option is null && (HotkeyCombo.IsCombo(saved) || HotkeyNames.IsNamedKey(saved)))
                option = AddRecordedOption(saved);

            if (saved == 0 || option is null || !taken.Add(saved))
                option = options[0];

            if (option.VirtualKey != saved)
            {
                SaveSelectionHotkey(action, option.VirtualKey);
                changed = true;
            }

            _backend.SetSelectionHotkey(action, option.VirtualKey);
            rows.Add(new SelectionHotkeyViewModel(
                action, label, hint, options, option, OnSelectionHotkeyChanged, _capture, AddRecordedOption));
        }

        if (changed)
            _store.Save(_settings);

        return rows;
    }

    private void OnSelectionHotkeyChanged(SelectionHotkeyViewModel row)
    {
        var key = row.Selected.VirtualKey;
        ApplyRowKey(row, key);

        if (key != 0)
        {

            foreach (var other in HotkeyRows)
                if (other != row && other.Selected.VirtualKey == key)
                    TurnOff(other);

            if (SelectedHotkey.VirtualKey == key)
                SelectedHotkey = Hotkeys.FirstOrDefault(h => !IsKeyTaken(h.VirtualKey)) ?? Hotkeys[0];
        }

        _store.Save(_settings);
        _changed();
    }

    private void TurnOff(SelectionHotkeyViewModel row)
    {
        row.SetSilently(row.Off);
        ApplyRowKey(row, row.Off.VirtualKey);
    }

    private bool IsKeyTaken(int virtualKey)
    {
        return HotkeyRows.Any(r => r.Selected.VirtualKey == virtualKey);
    }

    private IEnumerable<SelectionHotkeyViewModel> HotkeyRows =>
        DictationHotkeyRow is { } dictation ? SelectionActions.Append(dictation) : SelectionActions;

    private void ApplyRowKey(SelectionHotkeyViewModel row, int key)
    {
        if (row.Action is { } action)
        {
            SaveSelectionHotkey(action, key);
            _backend.SetSelectionHotkey(action, key);
            return;
        }

        _settings.DictationHotkey = key;
        ApplyDictationHotkey();
        _dictationKeyChanged();
    }

    public bool DictationSupported =>
        _supported && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) && _backend is IDictationHost;

    public SelectionHotkeyViewModel? DictationHotkeyRow { get; }

    private SelectionHotkeyViewModel? BuildDictationHotkeyRow()
    {
        if (!DictationSupported)
            return null;

        // Only dictation understands modifiers held alone, so only it offers them.
        var options = _dictationKeyOptions;
        options.Insert(1, new HotkeyOption(HotkeyNames.Format(AppSettings.DictationChord), AppSettings.DictationChord));
        var saved = _settings.DictationHotkey;
        var option = options.FirstOrDefault(o => o.VirtualKey == saved);
        if (option is null && (HotkeyCombo.IsCombo(saved) || HotkeyNames.IsNamedKey(saved)))
            option = AddRecordedOption(saved);

        // A key another action already has leaves dictation without one rather than firing both.
        if (option is null || saved == SelectedHotkey.VirtualKey || SelectionActions.Any(r => r.Selected.VirtualKey == saved))
            option = options[0];
        if (option.VirtualKey != saved)
        {
            _settings.DictationHotkey = option.VirtualKey;
            _store.Save(_settings);
        }

        var row = new SelectionHotkeyViewModel(
            null, "Voice_Toggle", "Dictation_HotkeyHint", options, option, OnSelectionHotkeyChanged, _capture,
            AddRecordedOption);
        ApplyDictationHotkey();
        return row;
    }

    public void ApplyDictationHotkey()
    {
        if (_backend is IDictationHost host)
            host.DictationHotkey = DictationSupported && _settings.DictationEnabled && _paidFeatures()
                ? _settings.DictationHotkey
                : 0;
    }

    private int SavedSelectionHotkey(SelectionAction action)
    {
        return action switch
        {
            SelectionAction.Correct => _settings.SelectionHotkeyVirtualKey,
            SelectionAction.CycleCase => _settings.CaseHotkeyVirtualKey,
            SelectionAction.Transliterate => _settings.TransliterateHotkeyVirtualKey,
            _ => _settings.ReverseRtlHotkeyVirtualKey
        };
    }

    private void SaveSelectionHotkey(SelectionAction action, int virtualKey)
    {
        switch (action)
        {
            case SelectionAction.Correct:
                _settings.SelectionHotkeyVirtualKey = virtualKey;
                break;
            case SelectionAction.CycleCase:
                _settings.CaseHotkeyVirtualKey = virtualKey;
                break;
            case SelectionAction.Transliterate:
                _settings.TransliterateHotkeyVirtualKey = virtualKey;
                break;
            default:
                _settings.ReverseRtlHotkeyVirtualKey = virtualKey;
                break;
        }
    }
    private HotkeyOption ResolveHotkey(int savedVirtualKey)
    {
        if (OperatingSystem.IsMacOS() && savedVirtualKey == MacHotkeys.CtrlOptionSpace)
        {
            savedVirtualKey = HotkeyTriggers.DoubleShift;
            _settings.HotkeyVirtualKey = savedVirtualKey;
            _store.Save(_settings);
        }

        var match = Hotkeys.FirstOrDefault(h => h.VirtualKey == savedVirtualKey);
        if (match is not null)
            return match;

        if (HotkeyCombo.IsCombo(savedVirtualKey) || HotkeyNames.IsNamedKey(savedVirtualKey))
            return AddRecordedOption(savedVirtualKey);

        var fallback = Hotkeys[0];
        _settings.HotkeyVirtualKey = fallback.VirtualKey;
        _store.Save(_settings);
        return fallback;
    }

    // The list entry is returned so rows compare equal.
    private HotkeyOption AddRecordedOption(int trigger)
    {
        var option = _hotkeyOptions.FirstOrDefault(o => o.VirtualKey == trigger)
                     ?? new HotkeyOption(HotkeyNames.Format(trigger), trigger);

        if (!_hotkeyOptions.Contains(option))
            _hotkeyOptions.Add(option);
        if (!_selectionKeyOptions.Contains(option))
            _selectionKeyOptions.Add(option);
        if (!_dictationKeyOptions.Contains(option))
            _dictationKeyOptions.Add(option);

        return option;
    }

    // The settings window losing focus must not leave the keyboard hook swallowing keys.
    public void CancelHotkeyRecording()
    {
        _capture.CancelActive();
    }
    [ObservableProperty] private HotkeyOption _selectedHotkey;
    partial void OnSelectedHotkeyChanged(HotkeyOption value)
    {
        _settings.HotkeyVirtualKey = value.VirtualKey;
        _backend.HotkeyVirtualKey = value.VirtualKey;

        foreach (var row in HotkeyRows)
            if (row.Selected.VirtualKey == value.VirtualKey)
                TurnOff(row);

        _store.Save(_settings);
        _changed();
    }

    public void RefreshLabels()
    {
        foreach (var row in HotkeyRows)
            row.RefreshLabels();
    }
}
