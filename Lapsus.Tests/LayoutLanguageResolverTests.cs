using Lapsus.Input;

namespace Lapsus.Tests;

public class LayoutLanguageResolverTests
{
    [Theory]
    [InlineData("com.apple.keylayout.Russian", "ru")]
    [InlineData("com.apple.keylayout.Ukrainian", "uk")]
    [InlineData("com.apple.keylayout.Belarusian", "be")]
    [InlineData("com.apple.keylayout.ABC", "en")]
    [InlineData("com.apple.keylayout.British", "en")]
    [InlineData("com.apple.keylayout.Greek", "el")]
    [InlineData("com.apple.keylayout.Hebrew", "he")]
    [InlineData("com.apple.keylayout.Arabic", "ar")]
    [InlineData("com.apple.keylayout.Georgian-QWERTY", "ka")]
    public void A_known_mac_source_id_names_the_layout(string sourceId, string code)
    {
        Assert.Equal(code, LayoutLanguageResolver.FromMacInputSourceId(sourceId));
    }

    [Fact]
    public void An_unknown_mac_source_id_is_left_to_the_declared_language()
    {
        Assert.Null(LayoutLanguageResolver.FromMacInputSourceId("com.apple.keylayout.German"));
    }

    [Theory]
    [InlineData(0x0409, "en")]
    [InlineData(0x0419, "ru")]
    [InlineData(0x0422, "uk")]
    public void A_windows_lang_id_names_the_layout(int langId, string code)
    {
        Assert.Equal(code, LayoutLanguageResolver.FromWindowsLangId(new IntPtr(langId)));
    }
}

public class RunningAppsSupportTests
{
    [Fact]
    public void Listing_running_apps_does_not_throw()
    {
        if (!RunningApps.IsSupported)
            return;

        var apps = RunningApps.List();
        Assert.NotNull(apps);
    }
}
