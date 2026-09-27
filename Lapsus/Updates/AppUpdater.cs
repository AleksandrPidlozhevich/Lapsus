using System;
using System.Threading.Tasks;
using Lapsus.Licensing;
using Lapsus.Startup;
using Velopack;
using Velopack.Sources;

namespace Lapsus.Updates;

public enum UpdateCheckResult
{

    Ready,

    UpToDate,

    NotSupported
}

public sealed class AppUpdater
{

    private readonly UpdateManager? _manager = TryCreate();

    private UpdateInfo? _pending;

    // Store/MSIX: no self-update (Store updates it; policy forbids otherwise).
    public static bool CanSelfUpdate => !PackageIdentity.IsPackaged;

    private static UpdateManager? TryCreate()
    {
        if (!CanSelfUpdate)
            return null;

        try
        {

            return new UpdateManager(new GithubSource(LicenseLinks.Repo, null, false));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public event EventHandler? UpdateReadyToInstall;

    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    public async Task CheckSilentlyAsync()
    {
        try
        {
            await CheckNowAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {

            CrashLog.Log(ex, "update check");
        }
    }

    public async Task<UpdateCheckResult> CheckNowAsync()
    {

        if (_manager is not { IsInstalled: true } manager)
            return UpdateCheckResult.NotSupported;

        if (_pending is not null)
            return UpdateCheckResult.Ready;

        var update = await manager.CheckForUpdatesAsync().ConfigureAwait(true);
        if (update is null)
            return UpdateCheckResult.UpToDate;

        await manager.DownloadUpdatesAsync(update).ConfigureAwait(true);
        _pending = update;
        UpdateReadyToInstall?.Invoke(this, EventArgs.Empty);
        return UpdateCheckResult.Ready;
    }

    public void ApplyAndRestart()
    {
        if (_pending is { } update && _manager is { } manager)
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
