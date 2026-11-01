using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Spelling;
using Lapsus.Audio;
using Lapsus.Input;
using Lapsus.Input.Dictation;
using Lapsus.Licensing;
using Lapsus.Licensing.AppStore;
using Lapsus.Localization;
using Lapsus.Neural;
using Lapsus.Settings;
using Lapsus.Speech;
using Lapsus.Startup;
using Lapsus.Updates;
using Lapsus.ViewModels;
using Lapsus.Views;
using System;
using System.Threading.Tasks;

namespace Lapsus;

public partial class App : Application
{
    private IInputBackend? _backend;
    private SettingsViewModel? _settingsViewModel;
    private DictionaryStore? _dictionaryStore;
    private ModelStore? _modelStore;
    private BrainBuilder? _brain;
    private Window? _window;
    private TrayIcon? _trayIcon;
    private SingleInstance? _singleInstance;
    private WordExceptions? _exceptions;
    private AppExclusions? _excludedApps;
    private LayoutIndicator? _indicator;
    private DictationEngine? _dictationEngine;
    private DictationSession? _dictation;
    private DictationIndicator? _dictationIndicator;

    private StoreViewModel? _storeViewModel;
    private LicenseReminder? _licenseReminder;

    private Window? _onboardingWindow;
    private bool _exiting;
    private bool _creatingWindow;
    private readonly AppUpdater _updater = new();
    private NativeMenuItem? _updateItem;
    private UpdateNotice? _updateNotice;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    internal void AttachSingleInstance(SingleInstance instance)
    {
        _singleInstance = instance;
    }

    public override void OnFrameworkInitializationCompleted()
    {

        if (Design.IsDesignMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (OperatingSystem.IsMacOS() && !MacSandbox.IsSandboxed)
            _ = Task.Run(MacOsStartupRegistration.RefreshIfEnabled);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _dictionaryStore = new DictionaryStore();
            _modelStore = new ModelStore();
            var installed = _dictionaryStore.Installed();

            var store = new SettingsStore();
            var settings = store.Load();

            _exceptions = new WordExceptions(settings.ExcludedWords);
            _exceptions.Changed += (_, _) =>
            {
                settings.ExcludedWords = [.. _exceptions.Snapshot()];
                store.Save(settings);
            };

            _excludedApps = new AppExclusions(settings.ExcludedApps);
            _excludedApps.Changed += (_, _) =>
            {
                settings.ExcludedApps = [.. _excludedApps.Snapshot()];
                store.Save(settings);
            };

            _backend = InputBackendFactory.CreateForCurrentPlatform(
                new LayoutCorrector(new SpellChecker(), exceptions: _exceptions), _excludedApps);
            _brain = new BrainBuilder(_backend, _dictionaryStore, _modelStore, _exceptions);
            var supported = InputBackendFactory.IsSupported(_backend);

            ThemeApplier.Apply(this, settings.ThemePreference);
            Localizer.Instance.SetLanguage(settings.InterfaceLanguage);
            _indicator = new LayoutIndicator(_excludedApps);

            var licenses = new LicenseStore();
            var licenseStatus = new LicenseStatus(
                licenses, new ActivationStore(), new ActivationClient(), settings, store);
            var licenseViewModel = new LicenseViewModel(
                licenses, licenseStatus, ShowLicensePage, () => _licenseReminder?.Close());
            _licenseReminder = new LicenseReminder(licenseStatus, licenseViewModel);

            _storeViewModel = new StoreViewModel(StorePurchasesFactory.CreateForCurrentBuild());
            _ = _storeViewModel.RefreshAsync();

            _settingsViewModel = new SettingsViewModel(
                _backend, store, settings, supported, _dictionaryStore, _modelStore,
                _brain.RebuildAsync, _exceptions, _excludedApps, _indicator, _updater, licenseViewModel,
                _storeViewModel);
            _brain.Settings = _settingsViewModel;

            SetupDictation(settings, store);

            SetupTrayIcon(desktop);
            ActualThemeVariantChanged += (_, _) => ApplyThemedIcon();
            _singleInstance?.StartWatching(ShowWindow);

            // Opening the app again from Finder or Launchpad reaches the running copy as a reopen.
            // In the store build that is the only signal: Launch Services is its single-instance check.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                activatable.Activated += (_, e) =>
                {
                    if (e.Kind == ActivationKind.Reopen)
                        ShowWindow();
                };

            _updateNotice = new UpdateNotice(settings, store);
            _updater.UpdateReadyToInstall += (_, _) => Dispatcher.UIThread.Post(ShowUpdateAvailable);
            _ = CheckForUpdateLaterAsync();

            _ = _licenseReminder.CheckAsync();

            var nothingToCorrectWith =
                installed.Count == 0 && !settings.NeuralCorrectionEnabled && !settings.PlainLayoutSwitch;
            if (!nothingToCorrectWith)
                _ = _brain.RebuildAsync();

            if (_settingsViewModel.OnboardingPending)
                Dispatcher.UIThread.Post(ShowOnboarding, DispatcherPriority.Loaded);
            else if (nothingToCorrectWith)
                Dispatcher.UIThread.Post(ShowWindow, DispatcherPriority.Loaded);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Voice typing. Nothing heavy happens here: the speech model loads on the first dictation (or, with "never
    // unload", half a minute after start), and the microphone opens only while recording.
    private void SetupDictation(AppSettings settings, SettingsStore store)
    {
        if (_backend is not IDictationHost host || _settingsViewModel is null || _dictionaryStore is null)
            return;

        var audio = AudioCaptureFactory.CreateForCurrentPlatform(() => _settingsViewModel.SettingsSnapshot().MicrophoneName);
        var supported = audio is not NullAudioCapture;
        var speech = new SpeechModelStore();
        var dictionaries = _dictionaryStore;
        _dictationEngine = new DictationEngine(speech, _settingsViewModel.SettingsSnapshot, dictionaries.Installed);
        string HotkeyName() => HotkeyNames.Format(settings.DictationHotkey);
        _dictationIndicator = new DictationIndicator(_indicator,
            () => Localizer.Instance.Format("Voice_ListeningHint", HotkeyName()),
            () => settings.DictationSounds);
        _dictation = new DictationSession(host, audio, _dictationEngine, _dictationIndicator,
            () => _settingsViewModel.SettingsSnapshot().DictationEnabled);
        _dictationIndicator.ButtonClicked = _dictation.Toggle;

        // Shown while voice typing is on (and not locked); its label follows the hotkey.
        void UpdateButton() => _dictationIndicator?.SetButton(
            supported && OperatingSystem.IsWindows() && settings.DictationEnabled && settings.DictationShowButton
            && !_settingsViewModel.NeuralLocked,
            settings.DictationHotkey == 0 ? "" : HotkeyName(),
            Localizer.Instance.Format("Voice_ButtonTip", settings.DictationHotkey == 0 ? "—" : HotkeyName()));

        void Apply(bool enabled)
        {
            _settingsViewModel.ApplyDictationHotkey();
            UpdateButton();
            if (!enabled)
            {
                _dictation?.Cancel();
                _ = Task.Run(() => _dictationEngine?.Unload());
            }
            else if (settings.SpeechUnloadAfter == SpeechUnloadAfter.Never)
            {
                PreloadSpeechLater();
            }
        }

        _settingsViewModel.Voice = new VoiceViewModel(settings, store, speech, supported, Apply,
            modelReady: () => _dictationEngine?.Prepare(),
            releaseModel: () => _dictationEngine?.Unload(),
            locked: () => _settingsViewModel.NeuralLocked);
        // Hint is raised again whenever the hotkey or the interface language changes.
        _settingsViewModel.Voice.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VoiceViewModel.Hint))
                UpdateButton();
        };
        Apply(supported && settings.DictationEnabled);
    }

    private void PreloadSpeechLater()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_exiting)
                _dictationEngine?.Preload();
        };
        timer.Start();
    }

    // A silent update downloads a whole package. Wait until startup (dictionary load, hook install)
    // has settled so the download does not compete with it and make the machine stutter.
    private async Task CheckForUpdateLaterAsync()
    {
        await Task.Delay(TimeSpan.FromMinutes(3)).ConfigureAwait(true);
        await _updater.CheckSilentlyAsync().ConfigureAwait(true);
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var loc = Localizer.Instance;

        var showItem = new NativeMenuItem(loc["Tray_Settings"]);
        showItem.Click += (_, _) => ShowWindow();

        // Apple requires "Quit <App>" on macOS; Windows keeps "Exit".
        var exitKey = OperatingSystem.IsMacOS() ? "Tray_Quit" : "Tray_Exit";
        var exitItem = new NativeMenuItem(loc[exitKey]);
        exitItem.Click += (_, _) =>
        {
            PerformExitCleanup();
            desktop.Shutdown();
        };

        desktop.ShutdownRequested += (_, _) => PerformExitCleanup();

        _trayIcon = new TrayIcon
        {
            ToolTipText = loc["Tray_Tooltip"],
            Menu = new NativeMenu { Items = { showItem, exitItem } }
        };

        _trayIcon.Clicked += (_, _) => ToggleWindow();

        if (OperatingSystem.IsMacOS())
            MacOSProperties.SetIsTemplateIcon(_trayIcon, true);

        ApplyThemedIcon();

        loc.LanguageChanged += (_, _) =>
        {
            showItem.Header = loc["Tray_Settings"];
            exitItem.Header = loc[exitKey];
            _trayIcon.ToolTipText = _updateItem is null
                ? loc["Tray_Tooltip"]
                : UpdateNotice.TooltipText(loc, _updater.PendingVersion);
            if (_updateItem is not null)
                _updateItem.Header = UpdateNotice.MenuText(loc, _updater.PendingVersion);
        };

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void PerformExitCleanup()
    {
        if (_exiting)
            return;
        _exiting = true;

        ShutdownGuard.Arm();

        _licenseReminder?.Stop();

        try
        {
            _indicator?.Dispose();
        }
        catch
        {
        }

        try
        {
            _dictation?.Dispose();
            _dictationIndicator?.Dispose();
            // Like the LLM: native threads may still be decoding, so abandon rather than free.
            _dictationEngine?.AbandonForShutdown();
        }
        catch
        {
        }

        try
        {
            _backend?.Dispose();
        }
        catch
        {
        }

        try
        {
            // Do not Dispose here: that waits/frees natives while ORT threads may still be inside.
            _brain?.AbandonForShutdown();
        }
        catch
        {
        }
    }

    // The silent check (or the Settings button) has downloaded an update. The tray keeps a menu item, a
    // tooltip and a dot on the icon until it is installed; Windows also gets one balloon per version.
    // Store builds never get here: their AppUpdater cannot check, so the event never fires.
    private void ShowUpdateAvailable()
    {
        if (!AppUpdater.CanSelfUpdate || _exiting || _updateItem is not null || _trayIcon?.Menu is not { } menu)
            return;

        var loc = Localizer.Instance;
        var version = _updater.PendingVersion;

        _updateItem = new NativeMenuItem(UpdateNotice.MenuText(loc, version));
        _updateItem.Click += (_, _) => _updater.ApplyAndRestart();
        menu.Items.Insert(0, _updateItem);
        menu.Items.Insert(1, new NativeMenuItemSeparator());
        _trayIcon.ToolTipText = UpdateNotice.TooltipText(loc, version);
        ApplyThemedIcon();

        // Claimed even when nothing pops up, so the same version is not announced on a later start.
        if (_updateNotice?.TryClaimAnnouncement(version) != true || version is null)
            return;

        // Someone looking at Settings already sees the update button change.
        if (_window is { IsActive: true })
            return;

        if (OperatingSystem.IsWindows())
            WindowsTrayBalloon.TryShow(_trayIcon, UpdateNotice.AnnouncementTitle(loc, version),
                UpdateNotice.AnnouncementBody(loc));
    }

    private void ApplyThemedIcon()
    {
        if (_trayIcon is null)
            return;

        var badge = _updateItem is not null;
        _trayIcon.Icon = OperatingSystem.IsMacOS()
            ? AppIcons.CreateTrayTemplateIcon(badge)
            : AppIcons.CreateTrayIcon(ActualThemeVariant, badge);

        if (_window is not null)
            _window.Icon = AppIcons.CreateWindowIcon(ActualThemeVariant);
    }

    private void ToggleWindow()
    {
        if (_window is { IsVisible: true })
            _window.Hide();
        else
            ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }

        if (_creatingWindow)
            return;

        if (_window is null)
        {
            _creatingWindow = true;
            try
            {
                _window = new SettingsWindow { DataContext = _settingsViewModel };
                ApplyThemedIcon();
                _window.Closing += (_, e) =>
                {
                    if (_exiting)
                        return;
                    e.Cancel = true;
                    _window!.Hide();
                };
            }
            finally
            {
                _creatingWindow = false;
            }
        }

        _window.Show();
        _window.Activate();
    }

    private void ShowOnboarding()
    {
        if (_onboardingWindow is not null)
        {
            _onboardingWindow.Activate();
            return;
        }

        var window = new OnboardingWindow();
        window.DataContext = new OnboardingViewModel(
            () =>
            {
                _settingsViewModel?.CompleteOnboarding();
                window.Close();
                // The permission banner and the dictionary library are both there.
                ShowWindow();
            },
            window.Close);
        window.Closed += (_, _) => _onboardingWindow = null;
        _onboardingWindow = window;
        window.Show();
        window.Activate();
    }

    private void ShowLicensePage()
    {
        ShowWindow();
        (_window as SettingsWindow)?.ShowLicensePage();
    }
}
