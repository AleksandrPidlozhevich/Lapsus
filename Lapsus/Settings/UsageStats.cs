using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lapsus.Settings;

// What is written to stats.json: how many fixes each day, and nothing about what was typed.
public sealed class UsageCounts
{
    // Day ("2026-11-14") to fixes that day. Only the last DaysKept days are kept; the totals go on.
    public Dictionary<string, int> Layout { get; set; } = [];

    public Dictionary<string, int> Typos { get; set; } = [];

    public int TotalLayout { get; set; }

    public int TotalTypos { get; set; }
}

// One day's corrections. The chart reads this; the file still stores only the counts.
public readonly record struct DayCount(DateOnly Date, int Layout, int Typos);

// Counts of corrections, for the chart in Settings. Kept beside the settings and never sent anywhere.
public sealed class UsageStats
{
    private const int DaysKept = 60;

    private readonly string _path;
    private readonly Func<DateOnly> _today;
    private UsageCounts? _counts;
    private bool? _lastWasLayout;

    public UsageStats()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus", "stats.json"),
            () => DateOnly.FromDateTime(DateTime.Now))
    {
    }

    // Tests: a file and a calendar of their own.
    internal UsageStats(string path, Func<DateOnly> today)
    {
        _path = path;
        _today = today;
    }

    private UsageCounts Counts => _counts ??= Load();

    public int Total => Counts.TotalLayout + Counts.TotalTypos;

    public void Note(bool switchedLayout)
    {
        Add(switchedLayout, 1);
        _lastWasLayout = switchedLayout;
    }

    // The fix just counted was taken back with Backspace.
    public void Undo()
    {
        if (_lastWasLayout is not { } layout)
            return;

        Add(layout, -1);
        _lastWasLayout = null;
    }

    public (int Layout, int Typos) LastDays(int days)
    {
        var layout = 0;
        var typos = 0;
        foreach (var day in Days(days))
        {
            layout += day.Layout;
            typos += day.Typos;
        }

        return (layout, typos);
    }

    // Oldest day first, including days with nothing to fix, so the chart has a column for each of them.
    public IReadOnlyList<DayCount> Days(int days)
    {
        var counts = Counts;
        var today = _today();
        var list = new List<DayCount>(days);
        for (var i = days - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var key = Key(date);
            list.Add(new DayCount(date, counts.Layout.GetValueOrDefault(key), counts.Typos.GetValueOrDefault(key)));
        }

        return list;
    }

    private void Add(bool layout, int by)
    {
        var counts = Counts;
        var days = layout ? counts.Layout : counts.Typos;
        var day = Key(_today());
        days[day] = Math.Max(0, days.GetValueOrDefault(day) + by);
        if (layout)
            counts.TotalLayout = Math.Max(0, counts.TotalLayout + by);
        else
            counts.TotalTypos = Math.Max(0, counts.TotalTypos + by);

        Prune(counts.Layout);
        Prune(counts.Typos);
        Save(counts);
    }

    private void Prune(Dictionary<string, int> days)
    {
        // The keys sort as dates do.
        var oldest = Key(_today().AddDays(-DaysKept));
        foreach (var day in new List<string>(days.Keys))
            if (string.CompareOrdinal(day, oldest) < 0)
                days.Remove(day);
    }

    private static string Key(DateOnly day)
    {
        return day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private UsageCounts Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize(File.ReadAllText(_path), SettingsJsonContext.Default.UsageCounts)
                       ?? new UsageCounts();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A broken counter file starts the count again; it must never get in the way of typing.
        }

        return new UsageCounts();
    }

    private void Save(UsageCounts counts)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(counts, SettingsJsonContext.Default.UsageCounts));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
