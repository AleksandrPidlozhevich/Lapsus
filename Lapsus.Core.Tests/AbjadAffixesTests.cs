using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

public sealed class AbjadAffixesTests
{
    [Fact]
    public void Hebrew_prefix_comes_off_longest_first()
    {
        var stems = AbjadAffixes.Stems("והילדים", Script.Hebrew).ToArray();

        Assert.Equal("ילדים", stems[0]);
        Assert.Contains("הילדים", stems);
    }

    [Fact]
    public void Arabic_article_and_pronoun_both_come_off()
    {
        Assert.Contains("كتاب", AbjadAffixes.Stems("الكتاب", Script.Arabic));
        Assert.Contains("كتاب", AbjadAffixes.Stems("كتابها", Script.Arabic));
    }

    [Fact]
    public void A_word_too_short_to_survive_the_cut_has_no_stems()
    {
        Assert.Empty(AbjadAffixes.Stems("ולי", Script.Hebrew));
        Assert.Empty(AbjadAffixes.Stems("الكتب", Script.Arabic));
    }

    [Fact]
    public void Only_one_clitic_comes_off_at_a_time()
    {
        Assert.DoesNotContain("ילד", AbjadAffixes.Stems("והילדים", Script.Hebrew));
    }

    [Fact]
    public void Scripts_that_write_their_words_whole_have_no_clitics()
    {
        Assert.Empty(AbjadAffixes.Stems("привет", Script.Cyrillic));
        Assert.Empty(AbjadAffixes.Stems("hello", Script.Latin));
    }
}
