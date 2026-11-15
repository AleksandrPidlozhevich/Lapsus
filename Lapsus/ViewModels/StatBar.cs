using Avalonia;
using Lapsus.Localization;
using System;
using System.Globalization;

namespace Lapsus.ViewModels;

// One column of the week chart. Heights are pixels; zero stays empty so a quiet day is just its name.
public sealed class StatBar(DateOnly date, int layout, int typos, double layoutHeight, double typoHeight, bool isToday)
{
    public const double Plot = 72;
    public int Layout { get; } = layout;

    public int Typos { get; } = typos;

    public double LayoutHeight { get; } = layoutHeight;

    public double TypoHeight { get; } = typoHeight;

    public bool IsToday { get; } = isToday;

    public bool HasLayout => Layout > 0;

    public bool HasTypos => Typos > 0;

    public string CountText => Layout + Typos > 0 ? (Layout + Typos).ToString() : "";

    public CornerRadius LayoutRadius => HasTypos ? new(0, 0, 4, 4) : new(4);

    public CornerRadius TypoRadius => HasLayout ? new(4, 4, 0, 0) : new(4);

    public string Label => date.ToString("ddd", Culture);

    public string Tip => string.Format(
        Culture,
        "{0}: {1} {2}, {3} {4}",
        date.ToString("d", Culture),
        Layout,
        Localizer.Instance["Stats_Layout"],
        Typos,
        Localizer.Instance["Stats_Typos"]);

    private static CultureInfo Culture => CultureInfo.GetCultureInfo(Localizer.Instance.CurrentCode);
}
