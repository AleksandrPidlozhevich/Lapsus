using Lapsus.Localization;
using Lapsus.Settings;
using Lapsus.Updates;

namespace Lapsus.Tests;

public sealed class AppUpdateNoticeTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"lapsus-update-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }

    [Fact]
    public void Announces_a_version_once()
    {
        var notice = new UpdateNotice(new AppSettings(), new SettingsStore(_settingsPath));

        Assert.True(notice.TryClaimAnnouncement("1.4.0"));
        Assert.False(notice.TryClaimAnnouncement("1.4.0"));
    }

    [Fact]
    public void Does_not_announce_the_same_version_again_after_a_restart()
    {
        new UpdateNotice(new AppSettings(), new SettingsStore(_settingsPath)).TryClaimAnnouncement("1.4.0");

        var reloaded = new SettingsStore(_settingsPath);
        var afterRestart = new UpdateNotice(reloaded.Load(), reloaded);

        Assert.False(afterRestart.TryClaimAnnouncement("1.4.0"));
    }

    [Fact]
    public void Announces_a_newer_version_found_later()
    {
        var notice = new UpdateNotice(new AppSettings(), new SettingsStore(_settingsPath));
        notice.TryClaimAnnouncement("1.4.0");

        Assert.True(notice.TryClaimAnnouncement("1.5.0"));
        Assert.Equal("1.5.0", new SettingsStore(_settingsPath).Load().AnnouncedUpdateVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Announces_nothing_without_a_version(string? version)
    {
        var settings = new AppSettings();
        var notice = new UpdateNotice(settings, new SettingsStore(_settingsPath));

        Assert.False(notice.TryClaimAnnouncement(version));
        Assert.Null(settings.AnnouncedUpdateVersion);
    }

    [Fact]
    public async Task A_copy_that_cannot_update_itself_never_announces_an_update()
    {
        // Store builds and this test host alike: the check reports NotSupported and raises nothing,
        // so the tray never shows the update item, dot or notification.
        var updater = new AppUpdater();
        var raised = false;
        updater.UpdateReadyToInstall += (_, _) => raised = true;

        await updater.CheckNowAsync();
        await updater.CheckSilentlyAsync();

        Assert.False(raised);
        Assert.Null(updater.PendingVersion);
    }

    [Fact]
    public void Every_language_names_the_version_and_fits_the_windows_limits()
    {
        var loc = Localizer.Instance;
        var problems = new List<string>();
        try
        {
            foreach (var language in loc.Languages)
            {
                loc.SetLanguage(language.Code);
                const string version = "12.34.567";

                var menu = UpdateNotice.MenuText(loc, version);
                var tooltip = UpdateNotice.TooltipText(loc, version);
                var title = UpdateNotice.AnnouncementTitle(loc, version);
                var body = UpdateNotice.AnnouncementBody(loc);

                if (!menu.Contains(version) || !tooltip.Contains(version) || !title.Contains(version))
                    problems.Add($"{language.Code}: version missing");
                // NOTIFYICONDATA: szTip 128, szInfoTitle 64, szInfo 256 characters, terminator included.
                if (tooltip.Length > 127 || title.Length > 63 || body.Length > 255)
                    problems.Add($"{language.Code}: too long for the tray");
            }
        }
        finally
        {
            loc.SetLanguage("en");
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void Falls_back_to_the_plain_update_text_without_a_version()
    {
        var loc = Localizer.Instance;

        Assert.Equal(loc["Tray_UpdateAvailable"], UpdateNotice.MenuText(loc, null));
        Assert.Equal(loc["Tray_UpdateAvailable"], UpdateNotice.TooltipText(loc, null));
    }

    [Fact]
    public void The_avalonia_tray_internals_the_windows_balloon_reads_still_exist()
    {
        // If an Avalonia upgrade renames them, the balloon silently stops; this makes it loud instead.
        Assert.True(WindowsTrayBalloon.AvaloniaInternalsPresent());
    }
}
