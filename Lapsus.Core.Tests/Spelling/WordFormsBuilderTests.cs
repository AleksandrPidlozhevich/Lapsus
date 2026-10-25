using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Spelling;

public sealed class WordFormsBuilderTests
{
    [Fact]
    public void Keeps_every_form_of_a_wiktionary_entry_as_the_keyboard_types_it()
    {
        var builder = new WordFormsBuilder("he", Script.Hebrew);

        builder.AddWiktionary([
            """{"word": "כֶּלֶב", "forms": [{"form": "כְּלָבִים", "tags": ["plural"]}, {"form": "כַּלְבֵי־", "tags": ["construct"]},""" +
            """ {"form": "kélev", "tags": ["romanization"]}, {"form": "קֶטֶל", "tags": ["class"]}]}"""
        ]);

        Assert.Equal(["כלב", "כלבי", "כלבים"], Forms(builder));
    }

    [Fact]
    public void Drops_a_spelling_in_letters_the_language_does_not_write()
    {
        var builder = new WordFormsBuilder("he", Script.Hebrew);

        builder.AddUniMorph(["x\tװאָס\tV", "x\tshalom\tN", "שלום\tשלומות\tN;PL"]);

        Assert.Equal(["שלומות"], Forms(builder));
    }

    [Fact]
    public void The_built_pair_is_read_by_the_hunspell_layer()
    {
        var builder = new WordFormsBuilder("he", Script.Hebrew);
        builder.AddUniMorph(["כלב\tכלבינו\tN;PSS1P"]);
        var words = TestDictionaries.NewFile("he", ".dic");
        builder.Write(words);
        var list = TestDictionaries.Text("he", "שלום 10\n");

        var spell = new SpellChecker([new DictionarySource("he", list, Script.Hebrew, words)]);

        Assert.True(spell.IsLexiconWord("כלבינו", Script.Hebrew, "he"));
    }

    private static string[] Forms(WordFormsBuilder builder)
    {
        var words = TestDictionaries.NewFile("forms", ".dic");
        builder.Write(words);
        var lines = File.ReadAllLines(words);
        Assert.Equal(lines.Length - 1, int.Parse(lines[0]));
        return lines.Skip(1).ToArray();
    }

}
