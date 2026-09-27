using Lapsus.Updates;

namespace Lapsus.Tests;

public class AppUpdaterTests
{
    [Fact]
    public async Task Reports_that_an_uninstalled_copy_cannot_update_itself()
    {
        var updater = new AppUpdater();

        var result = await updater.CheckNowAsync();

        Assert.Equal(UpdateCheckResult.NotSupported, result);
        Assert.NotEqual(UpdateCheckResult.UpToDate, result);
    }

    [Fact]
    public async Task Reaches_no_network_when_there_is_nothing_to_update()
    {
        var updater = new AppUpdater();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await updater.CheckNowAsync();
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"check took {watch.Elapsed}");
    }

    [Fact]
    public void Has_no_pending_version_before_a_check_finds_one()
    {
        var updater = new AppUpdater();

        Assert.Null(updater.PendingVersion);
    }
}
