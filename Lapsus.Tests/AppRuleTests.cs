using Lapsus.Core.Correction;
using Lapsus.Input;
using Lapsus.ViewModels;

namespace Lapsus.Tests;

public class AppRuleTests
{
    [Fact]
    public void Ticking_hotkey_only_moves_an_app_from_off_altogether_to_fix_off_and_back()
    {
        var apps = new AppExclusions(["Code.exe"]);
        var model = new ExclusionsViewModel(new WordExceptions(), apps);
        var row = Assert.Single(model.ExcludedApps);
        Assert.False(row.HotkeyOnly);

        row.HotkeyOnly = true;
        Assert.False(apps.Contains("code"));
        Assert.True(apps.AutoFixOff.Contains("code"));

        row.HotkeyOnly = false;
        Assert.True(apps.Contains("code"));
        Assert.False(apps.AutoFixOff.Contains("code"));
    }

    [Fact]
    public void An_app_on_both_lists_counts_as_off_altogether_and_removing_it_clears_both()
    {
        var apps = new AppExclusions(["wt"]);
        apps.AutoFixOff.Add("wt");
        apps.AutoFixOff.Add("slack");
        var model = new ExclusionsViewModel(new WordExceptions(), apps);

        Assert.Equal(["slack", "wt"], model.ExcludedApps.Select(r => r.Name).Order());
        Assert.False(model.ExcludedApps.Single(r => r.Name == "wt").HotkeyOnly);
        Assert.True(model.ExcludedApps.Single(r => r.Name == "slack").HotkeyOnly);

        model.RemoveExcludedAppCommand.Execute("wt");

        Assert.False(apps.Contains("wt"));
        Assert.False(apps.AutoFixOff.Contains("wt"));
    }

    [Fact]
    public void A_list_of_hotkey_only_apps_is_made_only_when_asked_for_and_has_none_of_its_own()
    {
        var apps = new AppExclusions();

        Assert.True(apps.AutoFixOff.IsEmpty);
        Assert.True(apps.AutoFixOff.AutoFixOff.IsEmpty);
        Assert.Same(apps.AutoFixOff, apps.AutoFixOff);
    }
}
