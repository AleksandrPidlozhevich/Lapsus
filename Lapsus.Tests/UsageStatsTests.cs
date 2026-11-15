using Lapsus.Settings;

namespace Lapsus.Tests;

public sealed class UsageStatsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lapsus-stats-{Guid.NewGuid():N}.json");
    private DateOnly _today = new(2026, 11, 14);

    public void Dispose()
    {
        File.Delete(_path);
    }

    private UsageStats Stats() => new(_path, () => _today);

    [Fact]
    public void Fixes_are_counted_by_kind_and_survive_a_restart()
    {
        var stats = Stats();
        stats.Note(switchedLayout: true);
        stats.Note(switchedLayout: true);
        stats.Note(switchedLayout: false);

        var reloaded = Stats();
        Assert.Equal((2, 1), reloaded.LastDays(7));
        Assert.Equal(3, reloaded.Total);
    }

    [Fact]
    public void A_week_is_the_last_seven_days_and_the_total_goes_on()
    {
        var stats = Stats();
        stats.Note(true);

        _today = _today.AddDays(6);
        stats.Note(false);
        Assert.Equal((1, 1), stats.LastDays(7));

        _today = _today.AddDays(1);
        Assert.Equal((0, 1), stats.LastDays(7));
        Assert.Equal(2, stats.Total);
    }

    [Fact]
    public void Each_day_is_kept_separately_oldest_first()
    {
        var stats = Stats();
        stats.Note(true);
        stats.Note(true);

        _today = _today.AddDays(2);
        stats.Note(false);

        Assert.Equal(
        [
            new DayCount(new DateOnly(2026, 11, 13), 0, 0),
            new DayCount(new DateOnly(2026, 11, 14), 2, 0),
            new DayCount(new DateOnly(2026, 11, 15), 0, 0),
            new DayCount(new DateOnly(2026, 11, 16), 0, 1),
        ], stats.Days(4));
    }

    [Fact]
    public void A_fix_taken_back_is_not_counted_and_only_the_last_one_can_be()
    {
        var stats = Stats();
        stats.Note(true);
        stats.Note(false);

        stats.Undo();
        stats.Undo();

        Assert.Equal((1, 0), stats.LastDays(7));
        Assert.Equal(1, stats.Total);
    }

    [Fact]
    public void Old_days_are_dropped_from_the_file_but_not_from_the_total()
    {
        var stats = Stats();
        stats.Note(true);

        _today = _today.AddDays(100);
        stats.Note(true);

        Assert.DoesNotContain("2026-11-14", File.ReadAllText(_path));
        Assert.Equal(2, Stats().Total);
    }

    [Fact]
    public void A_broken_file_starts_the_count_again()
    {
        File.WriteAllText(_path, "not json");

        var stats = Stats();
        stats.Note(true);

        Assert.Equal(1, stats.Total);
    }
}
