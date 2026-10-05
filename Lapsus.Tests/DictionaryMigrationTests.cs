using Lapsus.Settings;
using System.Globalization;

namespace Lapsus.Tests;

public sealed class DictionaryMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lapsus-migrate-{Guid.NewGuid():N}");

    public DictionaryMigrationTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void An_outdated_installed_list_is_cleaned_once_and_marked_current()
    {
        var store = new DictionaryStore(_directory);
        var uk = DictionaryCatalog.Available.Single(d => d.Code == "uk");
        File.WriteAllText(store.PathFor("uk"), "привіт 900\nнeт 800\nвітаю 700\n");
        File.WriteAllText(Path.Combine(_directory, "uk.version"), (uk.Version - 1).ToString(CultureInfo.InvariantCulture));
        Assert.True(store.IsOutdated(uk));

        store.CleanOutdatedLists();

        Assert.DoesNotContain("нeт", File.ReadAllText(store.PathFor("uk")));
        Assert.Contains("привіт 900", File.ReadAllText(store.PathFor("uk")));
        Assert.False(store.IsOutdated(uk));
    }

    [Fact]
    public void A_current_list_is_not_touched()
    {
        var store = new DictionaryStore(_directory);
        var uk = DictionaryCatalog.Available.Single(d => d.Code == "uk");
        File.WriteAllText(store.PathFor("uk"), "нeт 800\n");
        File.WriteAllText(Path.Combine(_directory, "uk.version"), uk.Version.ToString(CultureInfo.InvariantCulture));

        store.CleanOutdatedLists();

        Assert.Contains("нeт 800", File.ReadAllText(store.PathFor("uk")));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
