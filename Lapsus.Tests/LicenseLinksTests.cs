using Lapsus.Licensing;

namespace Lapsus.Tests;

public class LicenseLinksTests
{
    [Fact]
    public void The_project_page_is_the_website()
    {
        Assert.Equal("https://getlapsus.com", LicenseLinks.Website);
        Assert.Equal("https://getlapsus.com/en/", LicenseLinks.Page("", "en"));
        Assert.Equal("https://getlapsus.com/uk/", LicenseLinks.Page("", "uk"));
    }

    [Theory]
    [InlineData("uk", "https://getlapsus.com/uk/pricing/")]
    [InlineData("en", "https://getlapsus.com/en/pricing/")]
    [InlineData("ru", "https://getlapsus.com/ru/pricing/")]
    [InlineData("UK", "https://getlapsus.com/uk/pricing/")]
    [InlineData("pl", "https://getlapsus.com/en/pricing/")]
    [InlineData("PL", "https://getlapsus.com/en/pricing/")]
    public void Pricing_follows_the_UI_language_when_the_site_has_it(string language, string expected)
    {
        Assert.Equal(expected, LicenseLinks.Page("pricing", language));
        Assert.Equal(expected, LicenseLinks.Page("pricing/", language));
    }

    [Fact]
    public void Buy_buttons_open_the_pricing_page()
    {
        Assert.Equal(LicenseLinks.Page("pricing"), LicenseLinks.BuyPro);
        Assert.Equal(LicenseLinks.Page("pricing"), LicenseLinks.BuyBusiness);
        Assert.Equal(LicenseLinks.Page("pricing"), LicenseLinks.Seats);
    }

    [Fact]
    public void Terms_still_point_at_the_commercial_use_page()
    {
        Assert.Equal($"{LicenseLinks.Repo}/blob/master/COMMERCIAL-USE.md", LicenseLinks.Terms);
    }

    [Fact]
    public void License_text_is_the_LICENSE_file_in_the_repo()
    {
        Assert.Equal($"{LicenseLinks.Repo}/blob/master/LICENSE", LicenseLinks.LicenseText);
    }

    [Fact]
    public void Updates_still_read_the_public_GitHub_repo()
    {
        Assert.Equal("https://github.com/AleksandrPidlozhevich/Lapsus", LicenseLinks.Repo);
    }
}
