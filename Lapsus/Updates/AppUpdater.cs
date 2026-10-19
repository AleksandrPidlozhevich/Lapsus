using Lapsus.Licensing;
using Lapsus.Startup;
using System;
using System.Threading.Tasks;
#if !MAS
using Velopack;
using Velopack.Sources;
#endif

namespace Lapsus.Updates;

public enum UpdateCheckResult
{

    Ready,

    UpToDate,

    NotSupported
}

#if MAS
// Mac App Store build: the store updates the app (guideline 2.4.5(vii)) and Velopack is not shipped.
public sealed class AppUpdater
{
    public static bool CanSelfUpdate => false;

    public event EventHandler? UpdateReadyToInstall
    {
        add { }
        remove { }
    }

    public string? PendingVersion => null;

    public Task CheckSilentlyAsync() => Task.CompletedTask;

    public Task<UpdateCheckResult> CheckNowAsync() => Task.FromResult(UpdateCheckResult.NotSupported);

    public void ApplyAndRestart()
    {
    }
}
#else
public sealed class AppUpdater
{

    private readonly UpdateManager? _manager = TryCreate();

    private UpdateInfo? _pending;

    // Store/MSIX and a sandboxed Mac build: no self-update (the store updates it; policy forbids otherwise).
    public static bool CanSelfUpdate => !PackageIdentity.IsPackaged && !MacSandbox.IsSandboxed;

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
#endif
