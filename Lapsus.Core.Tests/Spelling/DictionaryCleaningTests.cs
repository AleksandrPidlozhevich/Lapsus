using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using System.Text;
using WeCantSpell.Hunspell;

namespace Lapsus.Core.Tests.Spelling;

public sealed class DictionaryCleaningTests
{
    [Fact]
    public void Romanian_s_cedilla_and_comma_letters_are_one_word()
    {
        var spell = new SpellChecker([new DictionarySource("ro", TestDictionaries.Text("clean", "şi 4000\nştiu 900\ncasa 50\n"), Script.Latin)]);

        Assert.True(spell.IsKnownWord("și", Script.Latin, "ro"));
        Assert.True(spell.IsKnownWord("şi", Script.Latin, "ro"));
        Assert.True(spell.IsKnownWord("Știu", Script.Latin, "ro"));
        Assert.True(spell.TryCorrect("știuu", Script.Latin, out var fixedWord, out _, "ro"));
        Assert.Equal("știu", fixedWord);
    }

    [Fact]
    public void Turkish_keeps_its_cedilla()
    {
        var spell = new SpellChecker([new DictionarySource("tr", TestDictionaries.Text("clean", "şey 900\nbaşka 500\n"), Script.Latin)]);

        Assert.True(spell.IsKnownWord("şey", Script.Latin, "tr"));
        Assert.False(spell.IsKnownWord("șey", Script.Latin, "tr"));
        Assert.True(spell.TryCorrect("başkaa", Script.Latin, out var fixedWord, out _, "tr"));
        Assert.Equal("başka", fixedWord);
    }

    [Fact]
    public void Romanian_hunspell_knows_a_word_typed_on_the_legacy_keyboard()
    {
        var words = TestDictionaries.Hunspell("ro", "1\nștiință\n", "SET UTF-8\n");
        var spell = new SpellChecker([new DictionarySource("ro", TestDictionaries.Text("clean", "casa 50\n"), Script.Latin, words)]);

        Assert.True(spell.IsLexiconWord("ştiinţă", Script.Latin, "ro"));
        Assert.True(spell.IsLexiconWord("știință", Script.Latin, "ro"));
    }

    [Fact]
    public void A_word_only_the_neighbour_knows_leaves_the_list()
    {
        var ukrainian = WordList.CreateFromWords(["мене", "мені", "якщо", "пам'ятаю", "его"]);
        var russian = WordList.CreateFromWords(["что", "меня", "если", "его"]);

        var kept = NeighbourWords.Remove(
            ["что 900", "мене 800", "если 700", "якщо 600", "ятаю 500", "джон 400", "его 300", "#comment"],
            ukrainian, russian, out var removed);

        Assert.Equal(2, removed);
        Assert.Equal(["мене 800", "якщо 600", "ятаю 500", "джон 400", "его 300", "#comment"], kept);
    }

    [Fact]
    public void A_capitalised_word_of_its_own_language_is_its_own()
    {
        var ukrainian = WordList.CreateFromWords(["Київ", "Олег"]);
        var russian = WordList.CreateFromWords(["киев", "Київ", "Олег"]);

        Assert.False(NeighbourWords.IsNeighbours("київ", ukrainian, russian));
        Assert.False(NeighbourWords.IsNeighbours("олег", ukrainian, russian));
        Assert.True(NeighbourWords.IsNeighbours("киев", ukrainian, russian));
    }

    [Fact]
    public void A_name_of_its_own_does_not_keep_the_neighbour_s_common_word()
    {
        // Только (name form) must not keep Russian только ("only").
        var ukrainian = WordList.CreateFromWords(["Только"]);
        var russian = WordList.CreateFromWords(["только"]);

        Assert.True(NeighbourWords.IsNeighbours("только", ukrainian, russian));
    }

    [Fact]
    public void Russian_the_Ukrainian_dictionary_also_accepts_is_left_out_of_the_Ukrainian_list()
    {
        var list = TestDictionaries.Text("clean", "мені 900\nмне 800\nконечно 500\nзвісно 300\n");
        var ukrainian = new SpellChecker([new DictionarySource("uk", list, Script.Cyrillic)]);

        Assert.True(ukrainian.IsKnownWord("мені", Script.Cyrillic, "uk"));
        Assert.True(ukrainian.IsKnownWord("звісно", Script.Cyrillic, "uk"));
        Assert.False(ukrainian.IsKnownWord("мне", Script.Cyrillic, "uk"));
        Assert.False(ukrainian.IsKnownWord("конечно", Script.Cyrillic, "uk"));
    }

    [Fact]
    public void The_neighbour_list_is_one_language_s_own()
    {
        var russian = new SpellChecker([new DictionarySource("ru", TestDictionaries.Text("clean", "мне 800\nконечно 500\n"), Script.Cyrillic)]);

        Assert.True(russian.IsKnownWord("мне", Script.Cyrillic, "ru"));
        Assert.True(NeighbourWords.IsListed("uk", "мне"));
        Assert.False(NeighbourWords.IsListed("ru", "мне"));
        Assert.False(NeighbourWords.IsListed(null, "мне"));
    }

    [Fact]
    public void The_neighbour_list_keeps_the_halves_of_apostrophe_words()
    {
        Assert.False(NeighbourWords.IsListed("uk", "ять"));
        Assert.False(NeighbourWords.IsListed("uk", "явиться"));
    }

}
