using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Spelling;

public sealed class WordAffixesTests
{
    [Fact]
    public void Hebrew_prefix_comes_off_longest_first()
    {
        var stems = WordAffixes.Stems("והילדים", Script.Hebrew).ToArray();

        Assert.Equal("ילדים", stems[0]);
        Assert.Contains("הילדים", stems);
    }

    [Fact]
    public void Arabic_article_and_pronoun_both_come_off()
    {
        Assert.Contains("كتاب", WordAffixes.Stems("الكتاب", Script.Arabic));
        Assert.Contains("كتاب", WordAffixes.Stems("كتابها", Script.Arabic));
    }

    [Fact]
    public void A_word_too_short_to_survive_the_cut_has_no_stems()
    {
        Assert.Empty(WordAffixes.Stems("ולי", Script.Hebrew));
        Assert.Empty(WordAffixes.Stems("الكتب", Script.Arabic));
    }

    [Fact]
    public void Only_one_clitic_comes_off_at_a_time()
    {
        Assert.DoesNotContain("ילד", WordAffixes.Stems("והילדים", Script.Hebrew));
    }

    [Fact]
    public void Scripts_that_write_their_words_whole_have_no_clitics()
    {
        Assert.Empty(WordAffixes.Stems("привет", Script.Cyrillic));
        Assert.Empty(WordAffixes.Stems("hello", Script.Latin));
    }
}
