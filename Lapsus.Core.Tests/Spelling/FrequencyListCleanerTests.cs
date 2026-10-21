using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Spelling;

public sealed class FrequencyListCleanerTests
{
    [Fact]
    public void Entries_mixing_two_alphabets_are_removed_and_the_rest_kept()
    {
        var list = TestDictionaries.Text("clean", "привет 900\nнeт 800\nhello 700\nпo 600\nмоя 500\n");

        var removed = FrequencyListCleaner.CleanFile(list, Script.Cyrillic);

        Assert.Equal(2, removed);
        var text = File.ReadAllText(list);
        Assert.DoesNotContain("нeт", text);
        Assert.DoesNotContain("пo", text);
        Assert.Contains("привет 900", text);
        Assert.Contains("hello 700", text);
        Assert.Contains("моя 500", text);
    }

    [Fact]
    public void Latin_lists_are_left_alone_because_their_look_alike_entries_feed_the_english_model()
    {
        var list = TestDictionaries.Text("clean", "yοu 3225\nhello 700\n");

        Assert.Equal(0, FrequencyListCleaner.CleanFile(list, Script.Latin));
        Assert.Contains("yοu 3225", File.ReadAllText(list));
    }

    [Fact]
    public void A_clean_list_is_not_rewritten()
    {
        var list = TestDictionaries.Text("clean", "hello 700\nhelp 600\n");
        var before = File.GetLastWriteTimeUtc(list);

        Assert.Equal(0, FrequencyListCleaner.CleanFile(list, Script.Latin));
        Assert.Equal(before, File.GetLastWriteTimeUtc(list));
    }

    [Fact]
    public void Abjads_are_never_cleaned()
    {
        // A Hebrew entry with a Latin letter spans two alphabets, so the rule alone would remove it.
        var list = TestDictionaries.Text("clean", "ובtest 900\nשחבר 800\n");

        Assert.Equal(0, FrequencyListCleaner.CleanFile(list, Script.Hebrew));
        Assert.Contains("ובtest 900", File.ReadAllText(list));
    }

    [Fact]
    public void Case_variants_of_a_mixed_entry_are_all_removed()
    {
        var list = TestDictionaries.Text("clean", "Нeт 30\nнeт 20\nhello 10\n");

        Assert.Equal(1, FrequencyListCleaner.CleanFile(list, Script.Cyrillic));
        Assert.Contains("hello 10", File.ReadAllText(list));
        Assert.DoesNotContain("нeт", File.ReadAllText(list));
    }

}
