using Lapsus.Settings;
using Lapsus.ViewModels;

namespace Lapsus.Tests;

public sealed class OnboardingTests
{
    [Fact]
    public void The_permissions_page_is_there_only_while_macos_still_has_to_ask()
    {
        Assert.DoesNotContain(OnboardingStep.Privacy, OnboardingViewModel.StepsFor(permissionsPending: false));

        var pending = OnboardingViewModel.StepsFor(permissionsPending: true);
        Assert.Equal(OnboardingStep.Welcome, pending[0]);
        Assert.Equal(OnboardingStep.Privacy, pending[1]);
        Assert.Equal(OnboardingStep.Options, pending[^1]);
    }

    [Theory]
    [InlineData("dsnfyyz", "en", "uk")]
    [InlineData("ghbdtn", "en", "ru", "uk")]
    [InlineData("dkssud", null, "en", "ko")]
    [InlineData("ba8ev", "en", "hy")]
    [InlineData("helo", "en", "de")]
    [InlineData("helo")]
    public void The_field_to_try_asks_for_keys_that_spell_a_word_on_the_users_own_layout(
        string expected, params string?[] layoutLanguages)
    {
        Assert.Equal(expected, OnboardingViewModel.SampleFor(layoutLanguages));
    }

    [Fact]
    public void Only_a_first_launch_has_no_settings_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-first-run-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            Assert.False(store.Exists);

            store.Save(new AppSettings());
            Assert.True(store.Exists);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
