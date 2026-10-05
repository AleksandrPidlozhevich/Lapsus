using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Core.Models;
using Lapsus.Core.Spelling;
using Lapsus.Input;
using Lapsus.Licensing;
using Lapsus.Localization;
using Lapsus.Neural;
using Lapsus.Settings;
using Lapsus.Startup;
using Lapsus.Updates;
using Lapsus.ViewModels;
using Lapsus.Views;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus;

public partial class App : Application
{
    private IInputBackend? _backend;
    private SettingsViewModel? _settingsViewModel;
    private DictionaryStore? _dictionaryStore;
    private ModelStore? _modelStore;
    private OnnxGenAiLlm? _llm;
    private Window? _window;
    private TrayIcon? _trayIcon;
    private SingleInstance? _singleInstance;
    private WordExceptions? _exceptions;
    private AppExclusions? _excludedApps;
    private LayoutIndicator? _indicator;
    private LicenseStatus? _licenseStatus;
    private LicenseViewModel? _licenseViewModel;
    private Window? _reminderWindow;
    private DispatcherTimer? _reminderTimer;
    private DateTimeOffset _reminderShownAt;
    private readonly SemaphoreSlim _brainGate = new(1, 1);

    private (string Path, ComputeDevicePreference Device)? _loadedModel;
    private bool _exiting;
    private bool _creatingWindow;
    private readonly AppUpdater _updater = new();
    private NativeMenuItem? _updateItem;

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

        if (OperatingSystem.IsMacOS())
            _ = Task.Run(MacOsStartupRegistration.RefreshIfEnabled);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _dictionaryStore = new DictionaryStore();
            _modelStore = new ModelStore();
            _llm = new OnnxGenAiLlm();
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
            var supported = InputBackendFactory.IsSupported(_backend);

            ThemeApplier.Apply(this, settings.ThemePreference);
            Localizer.Instance.SetLanguage(settings.InterfaceLanguage);
            _indicator = new LayoutIndicator(_excludedApps);

            var licenses = new LicenseStore();
            _licenseStatus = new LicenseStatus(
                licenses, new ActivationStore(), new ActivationClient(), settings, store);
            _licenseViewModel = new LicenseViewModel(
                licenses, _licenseStatus, ShowLicensePage, CloseLicenseReminder);

            _settingsViewModel = new SettingsViewModel(
                _backend, store, settings, supported, _dictionaryStore, _modelStore,
                RebuildBrainAsync, _exceptions, _excludedApps, _indicator, _updater, _licenseViewModel);

            SetupTrayIcon(desktop);
            ActualThemeVariantChanged += (_, _) => ApplyThemedIcon();
            _singleInstance?.StartWatching(ShowWindow);

            _updater.UpdateReadyToInstall += (_, _) => ShowUpdateAvailable();
            _ = CheckForUpdateLaterAsync();

            _ = CheckLicenseAsync();

            if (installed.Count == 0 && !settings.NeuralCorrectionEnabled && !settings.PlainLayoutSwitch)

                Dispatcher.UIThread.Post(ShowWindow, DispatcherPriority.Loaded);
            else
                _ = RebuildBrainAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // A silent update downloads a whole package. Wait until startup (dictionary load, hook install)
    // has settled so the download does not compete with it and make the machine stutter.
    private async Task CheckForUpdateLaterAsync()
    {
        await Task.Delay(TimeSpan.FromMinutes(3)).ConfigureAwait(true);
        await _updater.CheckSilentlyAsync().ConfigureAwait(true);
    }

    private async Task RebuildBrainAsync()
    {
        if (_dictionaryStore is null || _modelStore is null || _backend is null || _llm is null
            || _settingsViewModel is null)
            return;

        await _brainGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var settings = _settingsViewModel.SettingsSnapshot();
            IPhraseCorrector corrector;

            _backend.SetCorrector(new InactiveCorrector(settings.NeuralCorrectionEnabled));

            if (!settings.NeuralCorrectionEnabled)
            {
                _loadedModel = null;
                _ = Task.Run(() => _llm.Unload());
            }

            if (settings.NeuralCorrectionEnabled)
            {
                if (settings.SelectedModelId is { } modelId && _modelStore.IsInstalled(modelId))
                {
                    var path = _modelStore.PathFor(modelId);
                    var display = _modelStore.Describe(modelId)?.DisplayName ?? modelId;
                    var plan = ExecutionPlan.Build(
                        settings.ComputeDevice,
                        ComputeDeviceProbe.Capabilities(),
                        _modelStore.InstalledVariant(modelId).Device,
                        _modelStore.DeclaredProviders(modelId));
                    try
                    {

                        var wanted = (path, settings.ComputeDevice);
                        if (!_llm.IsLoaded || _loadedModel != wanted)
                        {
                            _settingsViewModel.Models.ReportLoading(display);
                            _loadedModel = null;

                            await Task.Run(() => _llm.Load(path, plan)).ConfigureAwait(true);
                            _loadedModel = wanted;

                            WarmUpNeural(_backend.PreferredLayout);
                        }

                        corrector = new NeuralPhraseRewriter(_llm, await BuildAdviserAsync(), _exceptions,
                            spellingLanguages: SpellingLanguages.For(modelId));
                        _settingsViewModel.Models.ReportReady(display, _llm.ExecutionProvider);
                    }
                    catch (Exception ex)
                    {
                        _loadedModel = null;
                        await Task.Run(() => _llm.Unload()).ConfigureAwait(true);
                        _settingsViewModel.Models.ReportLoadFailed(ex.Message);

                        corrector = new NeuralPhraseRewriter(_llm, await BuildAdviserAsync(), _exceptions);
                    }
                }
                else
                {
                    _loadedModel = null;
                    await Task.Run(() => _llm.Unload()).ConfigureAwait(true);
                    _settingsViewModel.Models.ReportNeedsModel();
                    corrector = new NeuralPhraseRewriter(_llm, await BuildAdviserAsync(), _exceptions);
                }
            }
            else if (settings.PlainLayoutSwitch)
            {
                corrector = new PlainLayoutSwitcher(_exceptions);
            }
            else
            {
                var installed = _dictionaryStore.Installed();
                corrector = await Task
                    .Run(() => new LayoutCorrector(new SpellChecker(installed), exceptions: _exceptions))
                    .ConfigureAwait(true);
            }

            _backend.SetCorrector(corrector);

            _backend.AutoMode = settings.AutoMode;
        }
        finally
        {
            _brainGate.Release();
        }
    }

    private async Task<IPhraseCorrector?> BuildAdviserAsync()
    {
        if (_dictionaryStore is null)
            return null;

        var installed = _dictionaryStore.Installed();
        if (installed.Count == 0)
            return null;

        return await Task
            .Run(IPhraseCorrector () =>
                new LayoutCorrector(new SpellChecker(installed), exceptions: _exceptions))
            .ConfigureAwait(true);
    }

    private void WarmUpNeural(KeyboardLayout? preferred)
    {
        if (_llm is not { } llm || _backend is null)
            return;

        const string sample = "ntrcn";
        var system = NeuralRewritePrompt.BuildSystem(preferred);
        var user = NeuralRewritePrompt.BuildUser(
            sample, BundledKeyboardMaps.En, _backend.LayoutCandidates(), preferred);
        _ = llm.WarmUpAsync(system, user, sample.Length);
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var loc = Localizer.Instance;

        var showItem = new NativeMenuItem(loc["Tray_Settings"]);
        showItem.Click += (_, _) => ShowWindow();

        var exitItem = new NativeMenuItem(loc["Tray_Exit"]);
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
            exitItem.Header = loc["Tray_Exit"];
            _trayIcon.ToolTipText = loc["Tray_Tooltip"];
            if (_updateItem is not null)
                _updateItem.Header = loc["Tray_UpdateAvailable"];
        };

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void PerformExitCleanup()
    {
        if (_exiting)
            return;
        _exiting = true;

        ShutdownGuard.Arm();

        _reminderTimer?.Stop();
        _reminderTimer = null;

        try
        {
            _indicator?.Dispose();
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

        var llm = _llm;
        _llm = null;
        try
        {
            // Do not Dispose here: that waits/frees natives while ORT threads may still be inside.
            llm?.AbandonForShutdown();
        }
        catch
        {
        }
    }

    private void ShowUpdateAvailable()
    {
        if (_updateItem is not null || _trayIcon?.Menu is not { } menu)
            return;

        _updateItem = new NativeMenuItem(Localizer.Instance["Tray_UpdateAvailable"]);
        _updateItem.Click += (_, _) => _updater.ApplyAndRestart();
        menu.Items.Insert(0, _updateItem);
    }

    private void ApplyThemedIcon()
    {
        if (_trayIcon is null)
            return;

        _trayIcon.Icon = OperatingSystem.IsMacOS()
            ? AppIcons.CreateTrayTemplateIcon()
            : AppIcons.CreateWindowIcon(ActualThemeVariant);

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

    private static readonly TimeSpan ReminderInterval = TimeSpan.FromDays(1);

    private static readonly TimeSpan ReminderPollInterval = TimeSpan.FromMinutes(30);

    // License check is a reminder, never a feature gate.
    private async Task CheckLicenseAsync()
    {
        if (_licenseStatus is null || _licenseViewModel is null)
            return;

        var required = await _licenseStatus.RefreshAsync().ConfigureAwait(true);
        _licenseViewModel.OnStatusRefreshed();

        StartReminderTimer();

        if (!required)
            return;

        Dispatcher.UIThread.Post(ShowLicenseReminder, DispatcherPriority.Loaded);
    }

    private void StartReminderTimer()
    {
        if (_reminderTimer is not null)
            return;

        _reminderTimer = new DispatcherTimer { Interval = ReminderPollInterval };
        _reminderTimer.Tick += (_, _) => RemindIfDue();
        _reminderTimer.Start();
    }

    private void RemindIfDue()
    {
        if (_exiting || _licenseStatus is null)
            return;

        if (!_licenseStatus.LicenseRequired || _licenseStatus.IsEvaluating)
            return;

        if (DateTimeOffset.UtcNow - _reminderShownAt < ReminderInterval)
            return;

        ShowLicenseReminder();
    }

    private void ShowLicenseReminder()
    {

        _reminderShownAt = DateTimeOffset.UtcNow;

        if (_reminderWindow is not null)
        {
            _reminderWindow.Activate();
            return;
        }

        _reminderWindow = new LicenseReminderWindow { DataContext = _licenseViewModel };
        _reminderWindow.Closed += (_, _) => _reminderWindow = null;
        _reminderWindow.Show();
    }

    private void CloseLicenseReminder()
    {
        _reminderWindow?.Close();
        _reminderWindow = null;
    }

    private void ShowLicensePage()
    {
        ShowWindow();
        (_window as SettingsWindow)?.ShowLicensePage();
    }
}
