using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Correction;

public sealed class CapsSlipTests
{
    private static readonly List<LayoutCandidate> ToCyrillic =
        [new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")];

    private static readonly List<LayoutCandidate> ToLatin =
        [new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en")];

    private readonly LayoutCorrector _corrector = Corrector(new WordExceptions());

    private static LayoutCorrector Corrector(WordExceptions exceptions)
    {
        return new LayoutCorrector(
            new SpellChecker(
            [
                new DictionarySource("en", TestDictionaries.List(
                    "en", "hello 900", "world 800", "the 700", "how 600", "are 500", "you 400", "ids 300"), Script.Latin),
                new DictionarySource("uk", TestDictionaries.List(
                    "uk", "привіт 900", "світ 800", "як 700", "справи 600"), Script.Cyrillic)
            ]),
            exceptions: exceptions);
    }

    private string Latin(string typed)
    {
        return _corrector.CorrectPhrase(typed, Script.Latin, BundledKeyboardMaps.En, ToCyrillic).Corrected;
    }

    private string Cyrillic(string typed)
    {
        return _corrector.CorrectPhrase(typed, Script.Cyrillic, BundledKeyboardMaps.Uk, ToLatin).Corrected;
    }

    [Theory]
    [InlineData("hELLO", "Hello")]
    [InlineData("hELLO, wORLD!", "Hello, World!")]
    [InlineData("hELLO HOW ARE YOU", "Hello how are you")]
    [InlineData("hELLO HOW aRE YOU", "Hello how Are you")]
    public void Caps_lock_left_on_is_undone_from_the_word_that_shows_it(string typed, string expected)
    {
        Assert.Equal(expected, Latin(typed));
    }

    [Theory]
    [InlineData("пРИВІТ", "Привіт")]
    [InlineData("пРИВІТ, ЯК СПРАВИ", "Привіт, як справи")]
    public void Caps_lock_is_undone_in_cyrillic_too(string typed, string expected)
    {
        Assert.Equal(expected, Cyrillic(typed));
    }

    [Theory]
    [InlineData("gHBDSN", "Привіт")] // Caps Lock and the wrong layout at once
    [InlineData("GHbdsn", "Привіт")] // Shift held a letter too long, on the wrong layout
    public void A_slip_on_the_wrong_layout_is_put_right_together_with_the_layout(string typed, string expected)
    {
        Assert.Equal(expected, Latin(typed));
    }

    [Theory]
    [InlineData("HEllo", "Hello")]
    [InlineData("THe", "The")]
    [InlineData("HEllo WOrld", "Hello World")]
    public void A_second_capital_on_a_word_the_list_knows_is_dropped(string typed, string expected)
    {
        Assert.Equal(expected, Latin(typed));
    }

    [Theory]
    [InlineData("iOS")]
    [InlineData("mRNA")]
    [InlineData("eSIM")]
    [InlineData("IDs")]
    [InlineData("MYvar")]
    [InlineData("HELLO WORLD")]
    [InlineData("McDonald")]
    [InlineData("iPhone")]
    public void Names_acronyms_and_shouting_are_not_slips(string typed)
    {
        var result = _corrector.CorrectPhrase(typed, Script.Latin, BundledKeyboardMaps.En, ToCyrillic);

        Assert.False(result.Changed);
        Assert.Equal(typed, result.Corrected);
    }

    [Fact]
    public void A_word_on_the_leave_alone_list_keeps_its_capitals()
    {
        var corrector = Corrector(new WordExceptions(["hELLO", "HEllo"]));

        Assert.Equal("hELLO", corrector.CorrectPhrase("hELLO", Script.Latin, BundledKeyboardMaps.En, ToCyrillic).Corrected);
        Assert.Equal("HEllo", corrector.CorrectPhrase("HEllo", Script.Latin, BundledKeyboardMaps.En, ToCyrillic).Corrected);
    }

    [Theory]
    [InlineData("hELLO")]
    [InlineData("HEllo")]
    public void Where_only_the_keys_may_be_re_read_a_slip_stays(string typed)
    {
        var source = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En, "en");
        var result = _corrector.CorrectPhrase(
            typed, source, [source], ToCyrillic, null, new CorrectionHints(KeysOnly: true));

        Assert.False(result.Changed);
    }

    [Theory]
    [InlineData("hELLO", true)]
    [InlineData("HEllo", true)]
    [InlineData("пРИВІТ", true)]
    [InlineData("myVar", false)]
    [InlineData("iPhone", false)]
    [InlineData("iOS", false)]
    [InlineData("IDs", false)]
    public void Auto_mode_reads_a_slip_and_still_skips_camel_case(string word, bool eligible)
    {
        Assert.Equal(eligible, AutoCorrectPolicy.IsEligible(word));
    }
}
