using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using System.Text;

namespace Lapsus.Core.Tests;

public sealed class LexiconTests : IDisposable
{
    private const string EnKeys = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
    private const string UkrainianKeys = "ʼйцукенгшщзхїфівапролджєячсмитьбю.";

    private static readonly KeyboardMap EnMap = new(EnKeys.ToCharArray());
    private static readonly KeyboardMap UkMap = new(UkrainianKeys.ToCharArray());

    private const string UkAffix = "SET UTF-8\nSFX A Y 2\nSFX A я ею я\nSFX A я і я\n";
    private const string UkWords = "1\nполуниця/A\n";

    private readonly List<string> _files = [];

    [Fact]
    public void Knows_a_form_the_frequency_list_lacks()
    {
        var spell = Ukrainian(lexicon: true);

        Assert.False(spell.IsKnownWord("полуницею", Script.Cyrillic, "uk"));
        Assert.True(spell.IsLexiconWord("полуницею", Script.Cyrillic, "uk"));
        Assert.False(spell.IsLexiconWord("полуницеюю", Script.Cyrillic, "uk"));
    }

    [Fact]
    public void A_short_word_is_the_frequency_list_s_to_answer_for()
    {
        var words = Write("2\nкіт\nкоти\n", NewPath(".dic"));
        Write("SET UTF-8\n", Path.ChangeExtension(words, ".aff"));
        var spell = new SpellChecker([new DictionarySource("uk", Write("кава 10\n"), Script.Cyrillic, words)]);

        Assert.False(spell.IsLexiconWord("кіт", Script.Cyrillic, "uk"));
        Assert.True(spell.IsLexiconWord("коти", Script.Cyrillic, "uk"));
        Assert.Equal(5, SpellChecker.MinLexiconLetters(Script.Arabic));
    }

    [Fact]
    public void Off_consults_nothing()
    {
        Assert.False(Ukrainian(lexicon: false).IsLexiconWord("полуницею", Script.Cyrillic, "uk"));
    }

    [Fact]
    public void Another_language_s_lexicon_is_no_evidence_for_this_one()
    {
        Assert.False(Ukrainian(lexicon: true).IsLexiconWord("полуницею", Script.Cyrillic, "ru"));
    }

    [Fact]
    public void A_word_the_lexicon_knows_is_not_spell_fixed_into_a_commoner_one()
    {
        var words = Write("1\nполуниця/B\n");
        Write("SET UTF-8\nSFX B Y 1\nSFX B я і я\n", Path.ChangeExtension(words, ".aff"));
        var list = Write("полиці 500\nполиця 400\n");

        var withLexicon = new WordScorer(new SpellChecker([new DictionarySource("uk", list, Script.Cyrillic, words)]));
        var withoutLexicon = new WordScorer(new SpellChecker(
            [new DictionarySource("uk", list, Script.Cyrillic, words)], lexicon: false));

        Assert.Equal(("полуниці", 0), withLexicon.SpellFix("полуниці", Script.Cyrillic, "uk"));
        Assert.Equal("полиці", withoutLexicon.SpellFix("полуниці", Script.Cyrillic, "uk").Text);
    }

    [Fact]
    public void A_wrong_layout_word_only_the_lexicon_knows_switches_to_it()
    {
        var corrector = new LayoutCorrector(Ukrainian(lexicon: true));

        var result = corrector.CorrectPhrase("gjkeybws", Script.Latin, EnMap, ToUkrainian);

        Assert.Equal("полуниці", result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
    }

    [Fact]
    public void It_also_settles_a_letter_on_the_full_stop_key()
    {
        var corrector = new LayoutCorrector(Ukrainian(lexicon: true));

        Assert.Equal("полуницею", corrector.CorrectPhrase("gjkeybwt.", Script.Latin, EnMap, ToUkrainian).Corrected);
    }

    private static readonly List<LayoutCandidate> ToUkrainian = [new(Script.Cyrillic, KeyboardLayout.Uk, UkMap, "uk")];

    [Fact]
    public void Reads_a_dictionary_in_its_own_legacy_encoding()
    {
        // el_GR is ISO 8859-7; .NET needs the code-page provider registered.
        var words = NewPath(".dic");
        var greek = Encoding.GetEncoding("ISO-8859-7", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        File.WriteAllBytes(words, greek.GetBytes("1\nκαλός\n"));
        Write("SET ISO8859-7\n", Path.ChangeExtension(words, ".aff"));
        var list = Write("νερό 10\n");

        var spell = new SpellChecker([new DictionarySource("el", list, Script.Greek, words)]);

        Assert.True(spell.IsLexiconWord("καλός", Script.Greek, "el"));
    }

    [Fact]
    public void A_missing_lexicon_costs_only_its_own_words()
    {
        var list = Write("кава 10\n");

        var spell = new SpellChecker([new DictionarySource("uk", list, Script.Cyrillic, NewPath(".dic"))]);

        Assert.True(spell.IsKnownWord("кава", Script.Cyrillic, "uk"));
        Assert.False(spell.IsLexiconWord("кава", Script.Cyrillic, "uk"));
    }

    private SpellChecker Ukrainian(bool lexicon)
    {
        var words = Write(UkWords, NewPath(".dic"));
        Write(UkAffix, Path.ChangeExtension(words, ".aff"));
        var english = Write("the 100\nand 90\n");
        var list = Write("кава 100\nмолоко 90\n");
        return new SpellChecker(
            [new DictionarySource("en", english, Script.Latin), new DictionarySource("uk", list, Script.Cyrillic, words)],
            lexicon: lexicon);
    }

    private string Write(string content, string? path = null)
    {
        path ??= NewPath(".txt");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string NewPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-lexicon-{Guid.NewGuid():N}{extension}");
        _files.Add(path);
        _files.Add(Path.ChangeExtension(path, ".aff"));
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _files)
            File.Delete(file);
    }
}
