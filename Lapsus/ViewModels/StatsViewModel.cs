using CommunityToolkit.Mvvm.ComponentModel;
using Lapsus.Settings;
using System;
using System.Collections.Generic;

namespace Lapsus.ViewModels;

// The Statistics tab: the last week's fixes as numbers and a chart, and the total since installing.
public sealed class StatsViewModel : ObservableObject
{
    private readonly UsageStats _stats = new();

    public int WeekLayout => _stats.LastDays(7).Layout;

    public int WeekTypos => _stats.LastDays(7).Typos;

    public int WeekTotal => WeekLayout + WeekTypos;

    public int TotalFixes => _stats.Total;

    public IReadOnlyList<StatBar> WeekDays
    {
        get
        {
            var days = _stats.Days(7);
            var peak = 1;
            foreach (var day in days)
                peak = Math.Max(peak, day.Layout + day.Typos);

            var today = days[^1].Date;
            var bars = new StatBar[days.Count];
            for (var i = 0; i < days.Count; i++)
            {
                var day = days[i];
                bars[i] = new StatBar(day.Date, day.Layout, day.Typos,
                    BarHeight(day.Layout, peak), BarHeight(day.Typos, peak), day.Date == today);
            }

            return bars;
        }
    }

    // A single fix still draws a bar; the fullest day fills the plot.
    private static double BarHeight(int count, int peak) =>
        count == 0 ? 0 : Math.Max(4, count * StatBar.Plot / peak);

    public void Note(bool switchedLayout)
    {
        _stats.Note(switchedLayout);
        Refresh();
    }

    // The fix just counted was taken back with Backspace.
    public void Undo()
    {
        _stats.Undo();
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(WeekTotal));
        OnPropertyChanged(nameof(WeekLayout));
        OnPropertyChanged(nameof(WeekTypos));
        OnPropertyChanged(nameof(TotalFixes));
        OnPropertyChanged(nameof(WeekDays));
    }
}
