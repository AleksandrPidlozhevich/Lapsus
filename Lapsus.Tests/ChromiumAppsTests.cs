using Lapsus.Input;

namespace Lapsus.Tests;

public class ChromiumAppsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lapsus-apps-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("Electron Framework.framework", true)]
    [InlineData("Chromium Embedded Framework.framework", true)]
    [InlineData("Google Chrome Framework.framework", true)]
    [InlineData("Brave Browser Framework.framework", true)]
    [InlineData("Safari Framework.framework", false)]
    [InlineData("Electron.framework", false)]
    [InlineData("Sparkle.framework", false)]
    public void Only_chromium_or_electron_frameworks_are_recognized(string name, bool expected)
    {
        Assert.Equal(expected, ChromiumApps.IsChromiumFrameworkName(name));
    }

    [Fact]
    public void The_frameworks_folder_is_found_from_an_app_executable()
    {
        Assert.Equal(
            Path.Combine("/Applications/Slack.app", "Contents", "Frameworks"),
            ChromiumApps.AppBundleFrameworks("/Applications/Slack.app/Contents/MacOS/Slack"));
    }

    [Fact]
    public void A_path_outside_an_app_bundle_has_no_frameworks_folder()
    {
        Assert.Null(ChromiumApps.AppBundleFrameworks("/usr/bin/true"));
    }

    [Fact]
    public void An_electron_app_bundle_on_disk_is_detected()
    {
        var frameworks = Path.Combine(_root, "Slack.app", "Contents", "Frameworks");
        Directory.CreateDirectory(Path.Combine(frameworks, "Electron Framework.framework"));

        Assert.True(ChromiumApps.IsChromiumExecutable(Path.Combine(_root, "Slack.app", "Contents", "MacOS", "Slack")));
    }

    [Fact]
    public void A_native_app_bundle_is_not_detected()
    {
        var frameworks = Path.Combine(_root, "Notes.app", "Contents", "Frameworks");
        Directory.CreateDirectory(Path.Combine(frameworks, "Sparkle.framework"));

        Assert.False(ChromiumApps.IsChromiumExecutable(Path.Combine(_root, "Notes.app", "Contents", "MacOS", "Notes")));
    }

    [Fact]
    public void A_missing_executable_path_is_not_detected()
    {
        Assert.False(ChromiumApps.IsChromiumExecutable(null));
        Assert.False(ChromiumApps.IsChromiumExecutable(string.Empty));
    }
}
