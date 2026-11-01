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
using Lapsus.Input;
using Lapsus.Input.Dictation;
using Lapsus.Localization;
using Lapsus.Neural;
using Lapsus.Settings;
using Lapsus.Startup;
using Lapsus.Updates;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

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
    private readonly LayoutIndicator _indicator;

    private bool _turningOffTheOtherBrain;

    private bool _skipBrainRebuild;

    private bool _rebuildingPreferences;

    public SettingsViewModel(
        IInputBackend backend, SettingsStore store, AppSettings settings, bool supported,
        DictionaryStore dictionaryStore, ModelStore modelStore, Func<Task> rebuildBrain,
        WordExceptions exceptions, AppExclusions excludedApps, LayoutIndicator indicator,
        AppUpdater updater, LicenseViewModel license, StoreViewModel storeAccess)
    {
        _backend = backend;
        _indicator = indicator;
        _store = store;
        _settings = settings;
        _supported = supported;
        _rebuildBrain = rebuildBrain;
        _startup = StartupRegistrationFactory.CreateForCurrentPlatform();

        var capture = new HotkeyCapture(_backend, () => Enabled, message =>
        {
            StatusText = message;
            RefreshPermissionState();
        });
        // Store is set later in this constructor; until then nothing is locked.
        Shortcuts = new ShortcutsViewModel(
            backend, store, settings, supported, capture, () => Store is not { PaidFeatures: false },
            () => Voice?.RefreshLabels(), UpdateStatus);
        Updates = new UpdatesViewModel(updater, message => StatusText = message);
        Appearance = new AppearanceViewModel(store, settings);

        _enabled = settings.Enabled;
        _autoMode = settings.AutoMode;
        _autoFixTypos = settings.AutoFixTypos;
        _switchSystemLayout = settings.SwitchSystemLayout;
        _hotkeyCyclesLayout = settings.HotkeyCyclesLayout;
        _runAtStartup = settings.RunAtStartup;
        // The App Store edition's trial may have ended while the app was closed.
        _neuralCorrectionEnabled = settings.NeuralCorrectionEnabled && storeAccess.PaidFeatures;
        if (_neuralCorrectionEnabled != settings.NeuralCorrectionEnabled)
        {
            settings.NeuralCorrectionEnabled = false;
            store.Save(settings);
        }

        _plainLayoutSwitch = settings.PlainLayoutSwitch;

        _skipPasswordFields = settings.SkipPasswordFields;
        _backend.SkipPasswordFields = _skipPasswordFields;
        _showLayoutIndicator = settings.ShowLayoutIndicator;
        _indicator.Enabled = _showLayoutIndicator;
        _backend.AutoMode = _autoMode;
        _backend.AutoFixTypos = _autoFixTypos;
        _backend.SwitchSystemLayout = _switchSystemLayout;
        _backend.HotkeyCyclesLayout = _hotkeyCyclesLayout;
        _backend.Corrected += OnCorrected;
        _backend.Diagnostic += OnDiagnostic;

        Localizer.Instance.LanguageChanged += OnLanguageChanged;

        Exclusions = new ExclusionsViewModel(exceptions, excludedApps);

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
        Store = storeAccess;
        Store.AccessChanged += (_, _) => OnStoreAccessChanged();

        BuildPreferences(settings.PreferredLayout);

        // macOS asks for Input Monitoring and Accessibility the moment capture starts. A first launch
        // explains why before that (App Store guideline 2.5.14), and capture waits for the answer.
        if (OperatingSystem.IsMacOS() && !settings.OnboardingDone)
        {
            OnboardingPending = !SystemPermissions.AccessibilityGranted || !SystemPermissions.InputMonitoringGranted;
            if (!OnboardingPending)
            {
                settings.OnboardingDone = true;
                store.Save(settings);
            }
        }

        if (!OnboardingPending)
            ApplyEnabled();
        ApplyRunAtStartup();
    }

    public bool OnboardingPending { get; private set; }

    // The user read the explanation and chose to go on: now macOS may ask.
    public void CompleteOnboarding()
    {
        if (!OnboardingPending)
            return;

        OnboardingPending = false;
        _settings.OnboardingDone = true;
        _store.Save(_settings);
        ApplyEnabled();
    }

    public AppSettings SettingsSnapshot()
    {
        return _settings;
    }

    public ModelsViewModel Models { get; }

    [ObservableProperty] private VoiceViewModel? _voice;

    public DictionariesViewModel Dictionaries { get; }

    public LicenseViewModel License { get; }

    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "?";

    private void EnableNeuralForInstalledModel()
    {
        if (NeuralLocked)
            return;

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

    public StoreViewModel Store { get; }

    public bool NeuralLocked => !Store.PaidFeatures;

    private void OnStoreAccessChanged()
    {
        OnPropertyChanged(nameof(NeuralLocked));
        // Voice typing is a paid feature in the App Store edition too.
        if (NeuralLocked && Voice is { Enabled: true } voice)
            voice.Enabled = false;
        Voice?.RefreshLabels();
        ApplyDictationHotkey();
        if (!NeuralLocked || !NeuralCorrectionEnabled)
            return;

        NeuralCorrectionEnabled = false;
        StatusText = L["Store_TrialEnded"];
    }

    [ObservableProperty] private bool _plainLayoutSwitch;

    public ShortcutsViewModel Shortcuts { get; }

    public UpdatesViewModel Updates { get; }

    public AppearanceViewModel Appearance { get; }

    public void ApplyDictationHotkey() => Shortcuts.ApplyDictationHotkey();

    // The settings window losing focus must not leave the keyboard hook swallowing keys.
    public void CancelHotkeyRecording() => Shortcuts.CancelHotkeyRecording();

    public ExclusionsViewModel Exclusions { get; }

    public void RefreshRunningApps() => Exclusions.RefreshRunningApps();

    public Localizer L => Localizer.Instance;

    public FlowDirection FlowDirection =>
        Localizer.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    [ObservableProperty] private IReadOnlyList<PreferenceOption> _preferences = [];

    [ObservableProperty] private bool _enabled;

    [ObservableProperty] private bool _autoMode;

    [ObservableProperty] private bool _autoFixTypos;

    [ObservableProperty] private bool _switchSystemLayout;

    [ObservableProperty] private bool _hotkeyCyclesLayout;

    [ObservableProperty] private bool _skipPasswordFields;

    [ObservableProperty] private bool _showLayoutIndicator;

    public bool IsLayoutIndicatorSupported => LayoutIndicator.IsSupported;

    [ObservableProperty] private bool _runAtStartup;

    [ObservableProperty] private PreferenceOption _selectedPreference = null!;

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PermissionsNeeded))]
    private bool _accessibilityGranted = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PermissionsNeeded))]
    private bool _inputMonitoringGranted = true;

    public bool PermissionsNeeded => !AccessibilityGranted || !InputMonitoringGranted;

    public bool IsSupported => _supported;

    public bool IsStartupSupported => _startup.IsSupported;

    public bool AutoModeSwitchEnabled => IsSupported && AutoModeAllowed;

    public bool AutoFixTyposSwitchEnabled => AutoModeSwitchEnabled;

    private bool AutoModeAllowed => !NeuralCorrectionEnabled && !PlainLayoutSwitch;

    public string? AutoModeBlockedHint =>
        !IsSupported ? null
        : NeuralCorrectionEnabled ? L["AutoCorrect_Blocked_Neural"]
        : PlainLayoutSwitch ? L["AutoCorrect_Blocked_Plain"]
        : null;

    public string CaptureLabel => L[Enabled ? "Capture_On" : "Capture_Off"];

    partial void OnEnabledChanged(bool value)
    {
        if (!value)
            CancelHotkeyRecording();

        _settings.Enabled = value;
        _store.Save(_settings);
        OnPropertyChanged(nameof(CaptureLabel));
        ApplyEnabled();
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
        OnPropertyChanged(nameof(AutoModeBlockedHint));
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

    partial void OnHotkeyCyclesLayoutChanged(bool value)
    {
        _settings.HotkeyCyclesLayout = value;
        _backend.HotkeyCyclesLayout = value;
        _store.Save(_settings);
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

    private void OnLanguageChanged(object? sender, EventArgs e)
    {

        BuildPreferences(_settings.PreferredLayout);
        Shortcuts.RefreshLabels();
        Models.RefreshLabels();
        Updates.RefreshLabels();
        OnPropertyChanged(nameof(AutoModeBlockedHint));

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

    private async void ApplyEnabled()
    {
        try
        {
            // macOS: Start can take seconds while the OS creates the event tap, so it runs on a worker.
            // Windows: the WH_KEYBOARD_LL hook must be installed on a thread that pumps messages (the UI
            // thread). On a pool thread Windows times out every key event and silently drops the hook.
            // Stop touches UI-owned typing state and stays on this thread.
            if (Enabled && OperatingSystem.IsMacOS())
                await Task.Run(() =>
                {
                    // Surfaces macOS's own permission dialogs on first use instead of only pointing at
                    // System Settings; a no-op once the user has already granted or denied them.
                    SystemPermissions.PromptForPermissionsIfNeeded();
                    _backend.Start();
                }).ConfigureAwait(true);
            else if (Enabled)
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

            // Store builds: the user can switch the item off in the system's own settings, and
            // enabling it may still be waiting for their approval there.
            if ((PackageIdentity.IsPackaged || MacSandbox.IsSandboxed)
                && _startup.IsEnabled is var actual && actual != RunAtStartup)
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
                ? L.Format("Status_NeuralHotkeyActive", Shortcuts.SelectedHotkey.Name)
                : L.Format("Status_NeuralFallbackActive", Shortcuts.SelectedHotkey.Name)
            : AutoMode && AutoModeAllowed
                ? L.Format("Status_AutoActive", Shortcuts.SelectedHotkey.Name)
                : L.Format("Status_HotkeyActive", Shortcuts.SelectedHotkey.Name);
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
        Dispatcher.UIThread.Post(() =>
        {
            StatusText = message;
            RefreshPermissionState();
        });
    }
}
