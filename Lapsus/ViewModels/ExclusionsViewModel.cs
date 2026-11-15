using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Core.Correction;
using Lapsus.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Lapsus.ViewModels;

public sealed partial class AppRule(string name, bool hotkeyOnly, Action<string, bool> changed) : ObservableObject
{
    public string Name { get; } = name;

    // The row is rebuilt from the lists after a change, so this setter only has to report it.
    [ObservableProperty] private bool _hotkeyOnly = hotkeyOnly;

    partial void OnHotkeyOnlyChanged(bool value) => changed(Name, value);
}

public sealed partial class ExclusionsViewModel : ObservableObject
{
    private readonly WordExceptions _exceptions;
    private readonly AppExclusions _excludedApps;

    public ExclusionsViewModel(WordExceptions exceptions, AppExclusions excludedApps)
    {
        _exceptions = exceptions;
        _excludedApps = excludedApps;

        RefreshExcludedWords();
        _exceptions.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshExcludedWords);

        RefreshExcludedApps();
    }

    public ObservableCollection<string> ExcludedWords { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddExcludedWordCommand))]
    private string _newExcludedWord = string.Empty;

    public bool HasExcludedWords => ExcludedWords.Count > 0;

    private bool CanAddExcludedWord()
    {
        return !string.IsNullOrWhiteSpace(NewExcludedWord);
    }

    [RelayCommand(CanExecute = nameof(CanAddExcludedWord))]
    private void AddExcludedWord()
    {
        _exceptions.Add(NewExcludedWord);
        NewExcludedWord = string.Empty;
    }

    [RelayCommand]
    private void RemoveExcludedWord(string? word)
    {
        if (word is not null)
            _exceptions.Remove(word);
    }

    private void RefreshExcludedWords()
    {
        ExcludedWords.Clear();
        foreach (var word in _exceptions.Snapshot())
            ExcludedWords.Add(word);

        OnPropertyChanged(nameof(HasExcludedWords));
    }

    // Off in an app altogether, or only the fix after Space (the hotkey still works there).
    public ObservableCollection<AppRule> ExcludedApps { get; } = [];

    private void SetHotkeyOnly(string name, bool hotkeyOnly)
    {
        if (hotkeyOnly)
        {
            _excludedApps.Remove(name);
            _excludedApps.AutoFixOff.Add(name);
        }
        else
        {
            _excludedApps.AutoFixOff.Remove(name);
            _excludedApps.Add(name);
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddExcludedAppCommand))]
    private string _newExcludedApp = string.Empty;

    public bool HasExcludedApps => ExcludedApps.Count > 0;

    private bool CanAddExcludedApp()
    {
        return !string.IsNullOrWhiteSpace(NewExcludedApp);
    }

    [RelayCommand(CanExecute = nameof(CanAddExcludedApp))]
    private void AddExcludedApp()
    {
        _excludedApps.Add(NewExcludedApp);
        NewExcludedApp = string.Empty;
        RefreshExcludedApps();
    }

    [RelayCommand]
    private void RemoveExcludedApp(string? name)
    {
        if (name is null)
            return;

        _excludedApps.Remove(name);
        _excludedApps.AutoFixOff.Remove(name);
        RefreshExcludedApps();
        RefreshRunningApps();
    }

    private void RefreshExcludedApps()
    {
        ExcludedApps.Clear();
        var full = _excludedApps.Snapshot();
        foreach (var name in full)
            ExcludedApps.Add(new AppRule(name, false, SetHotkeyOnly));

        // An app on both lists, as an imported file can leave it, counts as off altogether.
        foreach (var name in _excludedApps.AutoFixOff.Snapshot())
            if (!full.Contains(name))
                ExcludedApps.Add(new AppRule(name, true, SetHotkeyOnly));

        OnPropertyChanged(nameof(HasExcludedApps));
    }

    public ObservableCollection<RunningApp> RunningApps { get; } = [];

    public bool CanPickRunningApps => Lapsus.Input.RunningApps.IsSupported;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRunningAppCommand))]
    private RunningApp? _selectedRunningApp;

    public void RefreshRunningApps()
    {
        if (!CanPickRunningApps)
            return;

        var wasSelected = SelectedRunningApp?.ProcessName;

        RunningApps.Clear();
        foreach (var app in Lapsus.Input.RunningApps.List())
            if (!_excludedApps.Contains(app.ProcessName) && !_excludedApps.AutoFixOff.Contains(app.ProcessName))
                RunningApps.Add(app);

        SelectedRunningApp = RunningApps.FirstOrDefault(app => app.ProcessName == wasSelected);
    }

    private bool CanAddRunningApp()
    {
        return SelectedRunningApp is not null;
    }

    [RelayCommand(CanExecute = nameof(CanAddRunningApp))]
    private void AddRunningApp()
    {
        if (SelectedRunningApp is null)
            return;

        _excludedApps.Add(SelectedRunningApp.ProcessName);
        RefreshExcludedApps();
        RefreshRunningApps();
    }
}
