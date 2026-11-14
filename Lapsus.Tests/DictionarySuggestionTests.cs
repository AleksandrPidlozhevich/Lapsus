using Lapsus.Input;
using Lapsus.Settings;

namespace Lapsus.Tests;

public class DictionarySuggestionTests
{
    [Fact]
    public void A_layout_language_with_no_word_list_is_offered()
    {
        var missing = DictionaryCatalog.MissingFor(["en", "uk", "de"], code => code == "en");

        Assert.Equal(["uk", "de"], missing.Select(d => d.Code));
    }

    [Fact]
    public void A_language_the_catalog_does_not_have_is_not_offered()
    {
        Assert.Empty(DictionaryCatalog.MissingFor(["ja", null, "zz"], _ => false));
    }

    [Fact]
    public void Two_layouts_of_one_language_offer_it_once()
    {
        Assert.Single(DictionaryCatalog.MissingFor(["de", "DE"], _ => false));
    }

    // The low word of a Windows layout handle is its language id.
    [Theory]
    [InlineData(0x04070407, "de")]
    [InlineData(0x04150415, "pl")]
    [InlineData(0x04220422, "uk")]
    [InlineData(0x041A041A, "hr")]
    [InlineData(0x241A241A, "sr")]
    public void A_windows_layout_names_its_language(long hkl, string expected)
    {
        Assert.Equal(expected, LayoutLanguageResolver.IsoLanguageOf(new IntPtr(hkl)));
    }

    [Fact]
    public void Every_language_a_layout_can_name_this_way_is_spelled_as_the_catalog_spells_it()
    {
        // Hebrew and Indonesian have old two-letter codes (iw, in) some systems still report.
        Assert.Equal("he", LayoutLanguageResolver.IsoLanguageOf(new IntPtr(0x040D)));
        Assert.Equal("id", LayoutLanguageResolver.IsoLanguageOf(new IntPtr(0x0421)));
    }
}
