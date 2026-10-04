using Lapsus.Startup;

namespace Lapsus.Tests;

public class StartupRegistrationFilesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lapsus-plist-" + Guid.NewGuid());

    public StartupRegistrationFilesTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string PlistPath => Path.Combine(_directory, StartupRegistrationFiles.MacOsPlistFileName);

    [Fact]
    public void A_missing_login_item_is_not_created()
    {
        Assert.False(StartupRegistrationFiles.RefreshMacOsPlist(PlistPath, "/Applications/Lapsus.app/Contents/MacOS/Lapsus"));
        Assert.False(File.Exists(PlistPath));
    }

    [Fact]
    public void A_login_item_pointing_at_an_old_path_is_rewritten_to_the_running_one()
    {
        File.WriteAllText(PlistPath, StartupRegistrationFiles.BuildMacOsPlistContent("/old/place/Lapsus"));

        Assert.True(StartupRegistrationFiles.RefreshMacOsPlist(PlistPath, "/Applications/Lapsus.app/Contents/MacOS/Lapsus"));

        var content = File.ReadAllText(PlistPath);
        Assert.Contains("/Applications/Lapsus.app/Contents/MacOS/Lapsus", content);
        Assert.DoesNotContain("/old/place/Lapsus", content);
    }

    [Fact]
    public void A_login_item_that_is_already_current_is_not_touched()
    {
        const string exe = "/Applications/Lapsus.app/Contents/MacOS/Lapsus";
        File.WriteAllText(PlistPath, StartupRegistrationFiles.BuildMacOsPlistContent(exe));
        var stamp = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(PlistPath, stamp);

        Assert.False(StartupRegistrationFiles.RefreshMacOsPlist(PlistPath, exe));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(PlistPath));
    }

    [Fact]
    public void An_empty_executable_path_never_overwrites_the_login_item()
    {
        File.WriteAllText(PlistPath, StartupRegistrationFiles.BuildMacOsPlistContent("/old/Lapsus"));

        Assert.False(StartupRegistrationFiles.RefreshMacOsPlist(PlistPath, string.Empty));
        Assert.Contains("/old/Lapsus", File.ReadAllText(PlistPath));
    }
}
