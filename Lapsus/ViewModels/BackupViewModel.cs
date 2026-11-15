using CommunityToolkit.Mvvm.ComponentModel;
using Lapsus.Localization;
using Lapsus.Settings;
using Lapsus.Startup;
using System;

namespace Lapsus.ViewModels;

// The Backup tab: settings out to a file and back from one. An import takes effect at the next start.
public sealed partial class BackupViewModel(SettingsStore store, AppSettings settings) : ObservableObject
{
    private static Localizer L => Localizer.Instance;

    // The App Store sandbox has no entitlement for files the user picks.
    public bool IsSupported => !OperatingSystem.IsMacOS() || !MacSandbox.IsSandboxed;

    [ObservableProperty] private string _status = "";

    public void Export(string path)
    {
        Status = L[store.Export(settings, path) ? "Backup_Exported" : "Backup_ExportFailed"];
    }

    public void Import(string path)
    {
        Status = L[store.Import(path, settings) ? "Backup_Imported" : "Backup_ImportFailed"];
    }
}
