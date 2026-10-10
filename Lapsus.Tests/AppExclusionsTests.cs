using Lapsus.Input;

namespace Lapsus.Tests;

public sealed class AppExclusionsTests
{
    // The macOS hotkey gate (MacOSInputBackend.ShouldSwallowHotkey) skips pid classification
    // entirely when this is true, so it must reflect "nothing configured" exactly.
    [Fact]
    public void A_fresh_list_is_empty()
    {
        Assert.True(new AppExclusions().IsEmpty);
    }

    [Fact]
    public void Adding_an_app_makes_it_non_empty()
    {
        var exclusions = new AppExclusions();
        exclusions.Add("Slack");

        Assert.False(exclusions.IsEmpty);
    }

    [Fact]
    public void Removing_the_last_app_makes_it_empty_again()
    {
        var exclusions = new AppExclusions();
        exclusions.Add("Slack");
        exclusions.Remove("Slack");

        Assert.True(exclusions.IsEmpty);
    }
}
