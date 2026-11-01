using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Core.Correction;
using Lapsus.Input;
using System.Collections.ObjectModel;
using System.Linq;

namespace Lapsus.ViewModels;

// Words Lapsus never corrects and apps it never types into.
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

    public ObservableCollection<string> ExcludedApps { get; } = [];

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
        RefreshExcludedApps();
        RefreshRunningApps();
    }

    private void RefreshExcludedApps()
    {
        ExcludedApps.Clear();
        foreach (var name in _excludedApps.Snapshot())
            ExcludedApps.Add(name);

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
            if (!_excludedApps.Contains(app.ProcessName))
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
