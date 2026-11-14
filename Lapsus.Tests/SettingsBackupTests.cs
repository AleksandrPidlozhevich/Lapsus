using Lapsus.Settings;

namespace Lapsus.Tests;

public sealed class SettingsBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lapsus-backup-{Guid.NewGuid():N}");
    private readonly string _exported;
    private readonly SettingsStore _store;

    public SettingsBackupTests()
    {
        Directory.CreateDirectory(_dir);
        _exported = Path.Combine(_dir, "exported.json");
        _store = new SettingsStore(Path.Combine(_dir, "settings.json"));
    }

    public void Dispose()
    {
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void An_import_brings_the_choices_but_not_what_belongs_to_the_computer()
    {
        var theirs = new AppSettings
        {
            AutoMode = true,
            ExcludedWords = ["kubectl"],
            OrganizationDetectedOn = new DateOnly(2020, 1, 1),
            MicrophoneName = "Their headset"
        };
        Assert.True(_store.Export(theirs, _exported));

        var mine = new AppSettings { OrganizationDetectedOn = new DateOnly(2026, 11, 1), OnboardingDone = true };
        Assert.True(_store.Import(_exported, mine));

        var loaded = _store.Load();
        Assert.True(loaded.AutoMode);
        Assert.Equal(["kubectl"], loaded.ExcludedWords);
        Assert.Equal(new DateOnly(2026, 11, 1), loaded.OrganizationDetectedOn);
        Assert.True(loaded.OnboardingDone);
        Assert.Null(loaded.MicrophoneName);
    }

    [Fact]
    public void After_an_import_the_running_copy_saves_nothing_over_it()
    {
        _store.Export(new AppSettings { AutoMode = true }, _exported);
        var running = new AppSettings();
        _store.Import(_exported, running);

        _store.Save(running);

        Assert.True(_store.Load().AutoMode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[1, 2]")]
    [InlineData("not json")]
    public void A_file_that_is_not_settings_changes_nothing(string content)
    {
        _store.Save(new AppSettings { AutoMode = true });
        File.WriteAllText(_exported, content);

        Assert.False(_store.Import(_exported, new AppSettings()));

        Assert.True(_store.Load().AutoMode);
        _store.Save(new AppSettings());
        Assert.False(_store.Load().AutoMode);
    }
}
