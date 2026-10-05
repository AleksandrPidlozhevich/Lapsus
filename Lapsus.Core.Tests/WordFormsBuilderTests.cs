using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

public sealed class WordFormsBuilderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lapsus-forms-{Guid.NewGuid():N}");

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
        Directory.CreateDirectory(_dir);
        var words = Path.Combine(_dir, "he.dic");
        builder.Write(words);
        var list = Path.Combine(_dir, "he.txt");
        File.WriteAllText(list, "שלום 10\n");

        var spell = new SpellChecker([new DictionarySource("he", list, Script.Hebrew, words)]);

        Assert.True(spell.IsLexiconWord("כלבינו", Script.Hebrew, "he"));
    }

    private string[] Forms(WordFormsBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        var words = Path.Combine(_dir, $"{Guid.NewGuid():N}.dic");
        builder.Write(words);
        var lines = File.ReadAllLines(words);
        Assert.Equal(lines.Length - 1, int.Parse(lines[0]));
        return lines.Skip(1).ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }
}
