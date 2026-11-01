using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Localization;
using Lapsus.Updates;
using System;
using System.Threading.Tasks;

namespace Lapsus.ViewModels;

// The update button in the window's corner: check now, or install the update already downloaded.
public sealed partial class UpdatesViewModel : ObservableObject
{
    private readonly AppUpdater _updater;
    private readonly Action<string> _report;

    public UpdatesViewModel(AppUpdater updater, Action<string> report)
    {
        _updater = updater;
        _report = report;
        _updater.UpdateReadyToInstall += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            UpdateAvailable = true;
            UpdateVersion = _updater.PendingVersion;
        });
    }

    private static Localizer L => Localizer.Instance;

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
                    _report(L["Status_UpToDate"]);
                    break;
                case UpdateCheckResult.NotSupported:
                    _report(L["Status_UpdateNotSupported"]);
                    break;

            }
        }
        catch (Exception ex)
        {
            _report(L.Format("Status_UpdateCheckFailed", ex.Message));
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(UpdateButtonTooltip));
    }
}
