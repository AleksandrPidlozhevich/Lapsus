using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Lapsus.Input;
using Lapsus.Settings;
using System;

namespace Lapsus.ViewModels;

// A break from correcting: asked for from the tray, or a full-screen game. The capture itself is switched
// by whoever owns it, through onChanged.
public sealed partial class PauseViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly Action _onChanged;
    private readonly CapturePause _pause = new(() => DateTime.Now);

    private bool _applied;

    public PauseViewModel(AppSettings settings, SettingsStore store, Action onChanged)
    {
        _settings = settings;
        _store = store;
        _onChanged = onChanged;
        _inGames = settings.PauseInGames;

        // A pause runs out, a game starts or ends, and nothing else says so.
        _ = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => Check());
    }

    public bool IsPaused => _pause.IsPaused;

    // The tray menu follows this.
    public event EventHandler? Changed;

    public bool IsGameSupported => OperatingSystem.IsWindows();

    [ObservableProperty] private bool _inGames;

    partial void OnInGamesChanged(bool value)
    {
        _settings.PauseInGames = value;
        _store.Save(_settings);
        Check();
    }

    // null: until Resume.
    public void For(TimeSpan? duration)
    {
        _pause.For(duration);
        Check();
    }

    public void Resume()
    {
        _pause.Resume();
        Check();
    }

    private void Check()
    {
        _pause.GameRunning = InGames && OperatingSystem.IsWindows() &&
                             NativeMethods.SHQueryUserNotificationState(out var state) == 0 &&
                             state == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN;
        if (_pause.IsPaused == _applied)
            return;

        _applied = _pause.IsPaused;
        OnPropertyChanged(nameof(IsPaused));
        _onChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
