using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Core.Text;
using Lapsus.Localization;
using Lapsus.Input;
using Lapsus.Neural;
using Lapsus.Settings;
using Lapsus.Startup;
using Lapsus.Updates;

namespace Lapsus.ViewModels;

public sealed record HotkeyOption(string Name, int VirtualKey);

public sealed record PreferenceOption(string Name, KeyboardLayout? Layout);

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IInputBackend _backend;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly bool _supported;
    private readonly Func<Task> _rebuildBrain;
    private readonly IStartupRegistration _startup;
    private readonly WordExceptions _exceptions;
    private readonly AppExclusions _excludedApps;
    private readonly LayoutIndicator _indicator;
    private readonly AppUpdater _updater;

    private bool _turningOffTheOtherBrain;

    private bool _skipBrainRebuild;

    private bool _rebuildingPreferences;

    public SettingsViewModel(
        IInputBackend backend, SettingsStore store, AppSettings settings, bool supported,
        DictionaryStore dictionaryStore, ModelStore modelStore, Func<Task> rebuildBrain,
        WordExceptions exceptions, AppExclusions excludedApps, LayoutIndicator indicator,
        AppUpdater updater, LicenseViewModel license)
    {
        _backend = backend;
        _updater = updater;
        _updater.UpdateReadyToInstall += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            UpdateAvailable = true;
            UpdateVersion = _updater.PendingVersion;
        });
        _indicator = indicator;
        _exceptions = exceptions;
        _excludedApps = excludedApps;
        _store = store;
        _settings = settings;
        _supported = supported;
        _rebuildBrain = rebuildBrain;
        _startup = StartupRegistrationFactory.CreateForCurrentPlatform();

        _selectedHotkey = ResolveHotkey(settings.HotkeyVirtualKey);
        SelectionActions = BuildSelectionActions();
        _enabled = settings.Enabled;
        _autoMode = settings.AutoMode;
        _autoFixTypos = settings.AutoFixTypos;
        _switchSystemLayout = settings.SwitchSystemLayout;
        _runAtStartup = settings.RunAtStartup;
        _neuralCorrectionEnabled = settings.NeuralCorrectionEnabled;
        _plainLayoutSwitch = settings.PlainLayoutSwitch;
        _selectedInterfaceLanguage = Languages.FirstOrDefault(l => l.Code == settings.InterfaceLanguage)
                                     ?? Languages[0];
        _themePreference = settings.ThemePreference;
        if (Application.Current is { } app)
        {
            _uiThemeVariant = app.ActualThemeVariant;
            app.ActualThemeVariantChanged += OnUiThemeChanged;
        }

        _skipPasswordFields = settings.SkipPasswordFields;
        _backend.SkipPasswordFields = _skipPasswordFields;
        _showLayoutIndicator = settings.ShowLayoutIndicator;
        _indicator.Enabled = _showLayoutIndicator;
        _backend.HotkeyVirtualKey = _selectedHotkey.VirtualKey;
        _backend.AutoMode = _autoMode;
        _backend.AutoFixTypos = _autoFixTypos;
        _backend.SwitchSystemLayout = _switchSystemLayout;
        _backend.Corrected += OnCorrected;
        _backend.Diagnostic += OnDiagnostic;

        Localizer.Instance.LanguageChanged += OnLanguageChanged;

        RefreshExcludedWords();
        _exceptions.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshExcludedWords);

        RefreshExcludedApps();

        Dictionaries = new DictionariesViewModel(dictionaryStore, _rebuildBrain);
        Dictionaries.Installed.CollectionChanged += (_, _) => BuildPreferences(_settings.PreferredLayout);

        Models = new ModelsViewModel(
            modelStore, settings, store, _rebuildBrain,
            () => NeuralCorrectionEnabled, EnableNeuralForInstalledModel);
        Models.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModelsViewModel.IsModelLoaded))
                UpdateStatus();
        };

        License = license;

        BuildPreferences(settings.PreferredLayout);

        ApplyEnabled();
        ApplyRunAtStartup();
    }

    public AppSettings SettingsSnapshot()
    {
        return _settings;
    }

    public ModelsViewModel Models { get; }

    public DictionariesViewModel Dictionaries { get; }

    public LicenseViewModel License { get; }

    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "?";

    public bool CanSelfUpdate => AppUpdater.CanSelfUpdate;

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string? _updateVersion;
    [ObservableProperty] private bool _isCheckingForUpdate;

    public string UpdateButtonGlyph => IsCheckingForUpdate ? "…" : UpdateAvailable ? "↓" : "↻";

    public string UpdateButtonTooltip => UpdateAvailable ? L["Update_Tooltip"] : L["Update_CheckTooltip"];

    partial void OnUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(UpdateButtonGlyph));
        OnPropertyChanged(nameof(UpdateButtonTooltip));
    }

    partial void OnIsCheckingForUpdateChanged(bool value)
    {
        OnPropertyChanged(nameof(UpdateButtonGlyph));
    }

    [RelayCommand]
    private async Task CheckOrInstallUpdateAsync()
    {
        if (UpdateAvailable)
        {
            _updater.ApplyAndRestart();
            return;
        }

        if (IsCheckingForUpdate)
            return;

        IsCheckingForUpdate = true;
        try
        {
            switch (await _updater.CheckNowAsync().ConfigureAwait(true))
            {
                case UpdateCheckResult.UpToDate:
                    StatusText = L["Status_UpToDate"];
                    break;
                case UpdateCheckResult.NotSupported:
                    StatusText = L["Status_UpdateNotSupported"];
                    break;

            }
        }
        catch (Exception ex)
        {
            StatusText = L.Format("Status_UpdateCheckFailed", ex.Message);
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    private void EnableNeuralForInstalledModel()
    {

        _skipBrainRebuild = true;
        try
        {
            NeuralCorrectionEnabled = true;
        }
        finally
        {
            _skipBrainRebuild = false;
        }
    }

    [ObservableProperty] private bool _neuralCorrectionEnabled;

    [ObservableProperty] private bool _plainLayoutSwitch;

    public IReadOnlyList<HotkeyOption> Hotkeys =>
        OperatingSystem.IsMacOS()
            ?
            [
                new HotkeyOption("⌃⌥Space", MacHotkeys.CtrlOptionSpace),
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

    private IReadOnlyList<HotkeyOption> SelectionKeyOptions =>
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

    private IReadOnlyList<SelectionHotkeyViewModel> BuildSelectionActions()
    {
        var options = SelectionKeyOptions;
        var rows = new List<SelectionHotkeyViewModel>(SelectionActionRows.Length);

        var taken = new HashSet<int> { SelectedHotkey.VirtualKey };
        var changed = false;

        foreach (var (action, label, hint) in SelectionActionRows)
        {
            var saved = SavedSelectionHotkey(action);
            var option = options.FirstOrDefault(o => o.VirtualKey == saved);

            if (saved == 0 || option is null || !taken.Add(saved))
                option = options[0];

            if (option.VirtualKey != saved)
            {
                SaveSelectionHotkey(action, option.VirtualKey);
                changed = true;
            }

            _backend.SetSelectionHotkey(action, option.VirtualKey);
            rows.Add(new SelectionHotkeyViewModel(
                action, label, hint, options, option, OnSelectionHotkeyChanged));
        }

        if (changed)
            _store.Save(_settings);

        return rows;
    }

    private void OnSelectionHotkeyChanged(SelectionHotkeyViewModel row)
    {
        var key = row.Selected.VirtualKey;
        SaveSelectionHotkey(row.Action, key);
        _backend.SetSelectionHotkey(row.Action, key);

        if (key != 0)
        {

            foreach (var other in SelectionActions)
                if (other != row && other.Selected.VirtualKey == key)
                    TurnOff(other);

            if (SelectedHotkey.VirtualKey == key)
                SelectedHotkey = Hotkeys.FirstOrDefault(h => !IsKeyTaken(h.VirtualKey)) ?? Hotkeys[0];
        }

        _store.Save(_settings);
        UpdateStatus();
    }

    private void TurnOff(SelectionHotkeyViewModel row)
    {
        row.SetSilently(row.Off);
        SaveSelectionHotkey(row.Action, row.Off.VirtualKey);
        _backend.SetSelectionHotkey(row.Action, row.Off.VirtualKey);
    }

    private bool IsKeyTaken(int virtualKey)
    {
        return SelectionActions.Any(r => r.Selected.VirtualKey == virtualKey);
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

    public ObservableCollection<string> ExcludedWords { get; } = [];

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddExcludedWordCommand))]
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

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddExcludedAppCommand))]
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

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddRunningAppCommand))]
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

    private HotkeyOption ResolveHotkey(int savedVirtualKey)
    {
        var match = Hotkeys.FirstOrDefault(h => h.VirtualKey == savedVirtualKey);
        if (match is not null)
            return match;

        var fallback = Hotkeys[0];
        _settings.HotkeyVirtualKey = fallback.VirtualKey;
        _store.Save(_settings);
        return fallback;
    }

    public Localizer L => Localizer.Instance;

    public FlowDirection FlowDirection =>
        Localizer.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public IReadOnlyList<LanguageOption> Languages => Localizer.Instance.Languages;

    [ObservableProperty] private LanguageOption _selectedInterfaceLanguage;

    [ObservableProperty] private AppThemePreference _themePreference;

    private ThemeVariant _uiThemeVariant = ThemeVariant.Dark;

    public bool IsLightTheme => ThemePreference == AppThemePreference.Light;

    public bool IsDarkTheme => ThemePreference == AppThemePreference.Dark;

    public bool IsSystemTheme => ThemePreference == AppThemePreference.System;

    public string SunIconPath => AppIcons.ToggleIconPath("sun", _uiThemeVariant);

    public string MoonIconPath => AppIcons.ToggleIconPath("moon", _uiThemeVariant);

    public string SystemIconPath => AppIcons.ToggleIconPath("system", _uiThemeVariant);

    public string TrashIconPath => AppIcons.ToggleIconPath("trash", _uiThemeVariant);

    [RelayCommand]
    private void SetTheme(string mode)
    {
        ThemePreference = mode switch
        {
            "Light" => AppThemePreference.Light,
            "Dark" => AppThemePreference.Dark,
            _ => AppThemePreference.System
        };
    }

    [ObservableProperty] private IReadOnlyList<PreferenceOption> _preferences = [];

    [ObservableProperty] private bool _enabled;

    [ObservableProperty] private HotkeyOption _selectedHotkey;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AutoFixTyposSwitchEnabled))]
    private bool _autoMode;

    [ObservableProperty] private bool _autoFixTypos;

    [ObservableProperty] private bool _switchSystemLayout;

    [ObservableProperty] private bool _skipPasswordFields;

    [ObservableProperty] private bool _showLayoutIndicator;

    public bool IsLayoutIndicatorSupported => LayoutIndicator.IsSupported;

    [ObservableProperty] private bool _runAtStartup;

    [ObservableProperty] private PreferenceOption _selectedPreference = null!;

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PermissionsNeeded))]
    private bool _accessibilityGranted = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PermissionsNeeded))]
    private bool _inputMonitoringGranted = true;

    public bool PermissionsNeeded => !AccessibilityGranted || !InputMonitoringGranted;

    public bool IsSupported => _supported;

    public bool IsStartupSupported => _startup.IsSupported;

    public bool AutoModeSwitchEnabled => IsSupported && AutoModeAllowed;

    public bool AutoFixTyposSwitchEnabled => AutoModeSwitchEnabled && AutoMode;

    private bool AutoModeAllowed => !NeuralCorrectionEnabled && !PlainLayoutSwitch;

    public string CaptureLabel => L[Enabled ? "Capture_On" : "Capture_Off"];

    partial void OnEnabledChanged(bool value)
    {
        _settings.Enabled = value;
        _store.Save(_settings);
        OnPropertyChanged(nameof(CaptureLabel));
        ApplyEnabled();
    }

    partial void OnSelectedHotkeyChanged(HotkeyOption value)
    {
        _settings.HotkeyVirtualKey = value.VirtualKey;
        _backend.HotkeyVirtualKey = value.VirtualKey;

        foreach (var row in SelectionActions)
            if (row.Selected.VirtualKey == value.VirtualKey)
                TurnOff(row);

        _store.Save(_settings);
        UpdateStatus();
    }

    partial void OnAutoModeChanged(bool value)
    {
        _settings.AutoMode = value;
        _backend.AutoMode = value;
        _store.Save(_settings);
        UpdateStatus();
    }

    partial void OnAutoFixTyposChanged(bool value)
    {
        _settings.AutoFixTypos = value;
        _backend.AutoFixTypos = value;
        _store.Save(_settings);
    }

    partial void OnNeuralCorrectionEnabledChanged(bool value)
    {
        _settings.NeuralCorrectionEnabled = value;
        if (_turningOffTheOtherBrain)
            return;

        if (value)
            TurnOff(() => PlainLayoutSwitch = false);

        ApplyBrainChoice();
    }

    partial void OnPlainLayoutSwitchChanged(bool value)
    {
        _settings.PlainLayoutSwitch = value;
        if (_turningOffTheOtherBrain)
            return;

        if (value)
            TurnOff(() => NeuralCorrectionEnabled = false);

        ApplyBrainChoice();
    }

    private void TurnOff(Action theOtherBrain)
    {
        _turningOffTheOtherBrain = true;
        try
        {
            theOtherBrain();
        }
        finally
        {
            _turningOffTheOtherBrain = false;
        }
    }

    private void ApplyBrainChoice()
    {
        _store.Save(_settings);
        _backend.AutoMode = AutoMode;
        OnPropertyChanged(nameof(AutoModeSwitchEnabled));
        OnPropertyChanged(nameof(AutoFixTyposSwitchEnabled));
        UpdateStatus();
        if (!_skipBrainRebuild)
            _ = _rebuildBrain();
    }

    partial void OnSwitchSystemLayoutChanged(bool value)
    {
        _settings.SwitchSystemLayout = value;
        _backend.SwitchSystemLayout = value;
        _store.Save(_settings);
        UpdateStatus();
    }

    partial void OnSkipPasswordFieldsChanged(bool value)
    {
        _settings.SkipPasswordFields = value;
        _backend.SkipPasswordFields = value;
        _store.Save(_settings);
    }

    partial void OnShowLayoutIndicatorChanged(bool value)
    {
        _settings.ShowLayoutIndicator = value;
        _indicator.Enabled = value;
        _store.Save(_settings);
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        _settings.RunAtStartup = value;
        _store.Save(_settings);
        ApplyRunAtStartup();
    }

    partial void OnSelectedPreferenceChanged(PreferenceOption value)
    {

        if (value is null)
            return;

        _backend.PreferredLayout = value.Layout;

        if (_rebuildingPreferences)
            return;

        _settings.PreferredLayout = value.Layout;
        _store.Save(_settings);
    }

    partial void OnSelectedInterfaceLanguageChanged(LanguageOption value)
    {
        _settings.InterfaceLanguage = value.Code;
        _store.Save(_settings);
        Localizer.Instance.SetLanguage(value.Code);
    }

    partial void OnThemePreferenceChanged(AppThemePreference value)
    {
        _settings.ThemePreference = value;
        _store.Save(_settings);
        if (Application.Current is { } app)
            ThemeApplier.Apply(app, value);
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsSystemTheme));
    }

    private void OnUiThemeChanged(object? sender, EventArgs e)
    {
        if (Application.Current is not { } app)
            return;

        _uiThemeVariant = app.ActualThemeVariant;
        OnPropertyChanged(nameof(SunIconPath));
        OnPropertyChanged(nameof(MoonIconPath));
        OnPropertyChanged(nameof(SystemIconPath));
        OnPropertyChanged(nameof(TrashIconPath));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {

        BuildPreferences(_settings.PreferredLayout);
        foreach (var row in SelectionActions)
            row.RefreshLabels();
        Models.RefreshLabels();
        OnPropertyChanged(nameof(UpdateButtonTooltip));

        UpdateStatus();
    }

    private void BuildPreferences(KeyboardLayout? select)
    {
        var options = new List<PreferenceOption> { new(L["Pref_Auto"], null) };

        foreach (var descriptor in Dictionaries.Installed
                     .Select(d => (d.Descriptor, Layout: LayoutLanguage.ToKeyboardLayout(d.Descriptor.Code)))
                     .Where(x => x.Layout is not null)
                     .GroupBy(x => x.Layout!.Value)
                     .Select(g => g.First())
                     .OrderBy(x => x.Descriptor.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            options.Add(new PreferenceOption(descriptor.Descriptor.DisplayName, descriptor.Layout));

        _rebuildingPreferences = true;
        try
        {
            Preferences = options;
            SelectedPreference = Preferences.FirstOrDefault(p => p.Layout == select) ?? Preferences[0];
        }
        finally
        {
            _rebuildingPreferences = false;
        }

        _backend.PreferredLayout = SelectedPreference.Layout;
    }

    private void ApplyEnabled()
    {
        try
        {
            if (Enabled)
                _backend.Start();
            else
                _backend.Stop();
        }
        catch (Exception ex)
        {

            var detail = ex is TypeInitializationException { InnerException: { } inner } ? inner.Message : ex.Message;
            StatusText = L.Format("Status_StartFailed", detail);
            RefreshPermissionState();
            return;
        }

        RefreshPermissionState();
        UpdateStatus();
    }

    private void RefreshPermissionState()
    {
        AccessibilityGranted = SystemPermissions.AccessibilityGranted;
        InputMonitoringGranted = SystemPermissions.InputMonitoringGranted;
    }

    public void OnWindowReopened()
    {
        RefreshRunningApps();

        var wasNeeded = PermissionsNeeded;
        RefreshPermissionState();

        if (wasNeeded && !PermissionsNeeded && Enabled && !_backend.IsRunning)
            ApplyEnabled();
    }

    [RelayCommand]
    private void OpenAccessibilitySettings()
    {
        Lapsus.Input.SystemPermissions.OpenAccessibilitySettings();
    }

    [RelayCommand]
    private void OpenInputMonitoringSettings()
    {
        Lapsus.Input.SystemPermissions.OpenInputMonitoringSettings();
    }

    private void ApplyRunAtStartup()
    {
        if (!_startup.IsSupported)
            return;

        try
        {
            _startup.SetEnabled(RunAtStartup);

            if (PackageIdentity.IsPackaged && _startup.IsEnabled is var actual && actual != RunAtStartup)
                Dispatcher.UIThread.Post(() => RunAtStartup = actual);
        }
        catch (Exception ex)
        {
            StatusText = L.Format("Status_StartupFailed", ex.Message);
        }
    }

    private void UpdateStatus()
    {
        if (!_supported)
        {
            StatusText = L["Status_UnsupportedPlatform"];
            return;
        }

        if (!Enabled)
        {
            StatusText = L["Status_Disabled"];
            return;
        }

        StatusText = NeuralCorrectionEnabled
            ? Models.IsModelLoaded
                ? L.Format("Status_NeuralHotkeyActive", SelectedHotkey.Name)
                : L.Format("Status_NeuralFallbackActive", SelectedHotkey.Name)
            : AutoMode && AutoModeAllowed
                ? L.Format("Status_AutoActive", SelectedHotkey.Name)
                : L.Format("Status_HotkeyActive", SelectedHotkey.Name);
    }

    private void OnCorrected(object? sender, CorrectionResult result)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _indicator.Refresh();
            StatusText = L.Format("Status_Corrected", result.Original, result.CorrectedString);
        });
    }

    private void OnDiagnostic(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() => StatusText = message);
    }
}
