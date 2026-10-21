using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Spelling;

public class SpellCheckerTests
{
    [Fact]
    public void Without_a_dictionary_nothing_is_known()
    {
        var empty = new SpellChecker();
        Assert.False(empty.Has(Script.Latin));
        Assert.False(empty.IsKnownWord("cat", Script.Latin));
    }

    [Fact]
    public void Loads_a_downloaded_dictionary_from_disk()
    {
        var path = TestDictionaries.Text("uk", "вітання 500\nдякую 300\nмісто 200\n");
        var spell = new SpellChecker(new[] { new DictionarySource("uk", path, Script.Cyrillic) });

        Assert.True(spell.Has(Script.Cyrillic));
        Assert.True(spell.IsKnownWord("вітання", Script.Cyrillic));
        Assert.True(spell.TryCorrect("дяку", Script.Cyrillic, out var fixedWord, out _));
        Assert.Equal("дякую", fixedWord);
    }

    [Fact]
    public void A_list_big_enough_teaches_a_trigram_model_and_a_tiny_one_does_not()
    {
        var ruPath = TestDictionaries.List("ngram", SampleWords.RussianLines());
        var tinyPath = TestDictionaries.List("tiny", ["cat 10", "dog 5"]);
        var spell = new SpellChecker(
        [
            new DictionarySource("ru", ruPath, Script.Cyrillic),
            new DictionarySource("en", tinyPath, Script.Latin)
        ]);

        Assert.True(spell.HasNgrams(Script.Cyrillic));
        Assert.False(spell.HasNgrams(Script.Latin));
        Assert.True(spell.Naturalness("приветик", Script.Cyrillic) > spell.Naturalness("руддщ", Script.Cyrillic));

        var vowels = new SpellChecker([new DictionarySource("ru", ruPath, Script.Cyrillic)], ngrams: false);
        Assert.False(vowels.HasNgrams(Script.Cyrillic));
    }

    [Fact]
    public void Pools_a_greek_dictionary_under_the_greek_script()
    {
        var path = TestDictionaries.Text("el", "καλημέρα 500\nευχαριστώ 300\nνερό 200\n");
        var spell = new SpellChecker(new[] { new DictionarySource("el", path, Script.Greek) });

        Assert.True(spell.Has(Script.Greek));
        Assert.True(spell.IsKnownWord("νερό", Script.Greek));
        Assert.False(spell.IsKnownWord("νερό", Script.Latin));
    }

    [Fact]
    public void Pools_dictionaries_by_script_across_languages()
    {
        var ukPath = TestDictionaries.Text("uk-pool", "вітання 500\n");
        var ruPath = TestDictionaries.Text("ru-pool", "привет 500\n");
        var spell = new SpellChecker(
        [
            new DictionarySource("uk", ukPath, Script.Cyrillic),
            new DictionarySource("ru", ruPath, Script.Cyrillic)
        ]);

        Assert.True(spell.IsKnownWord("привет", Script.Cyrillic, "uk"));
        Assert.True(spell.IsKnownWord("вітання", Script.Cyrillic, "ru"));
        Assert.True(spell.IsKnownWord("привет", Script.Cyrillic));
        Assert.True(spell.IsKnownWord("вітання", Script.Cyrillic));
    }
    [Fact]
    public void A_correction_prefers_the_commoner_word_between_two_dictionaries_of_one_script()
    {
        var ruPath = TestDictionaries.List("ru-freq", ["кит 50", "дом 100000"]);
        var ukPath = TestDictionaries.List("uk-freq", ["кіт 9000", "хата 10000"]);
        var spell = new SpellChecker(
        [
            new DictionarySource("ru", ruPath, Script.Cyrillic),
            new DictionarySource("uk", ukPath, Script.Cyrillic)
        ]);

        Assert.True(spell.TryCorrect("кот", Script.Cyrillic, out var corrected, out var distance));
        Assert.Equal("кіт", corrected);
        Assert.Equal(1, distance);
    }

    [Theory]
    [InlineData("he", Script.Hebrew, "שָׁלוֹם 900", "שלום")]
    [InlineData("he", Script.Hebrew, "שלום 900", "שָׁלוֹם")]
    [InlineData("ar", Script.Arabic, "شكراً 900", "شكرا")]
    [InlineData("ar", Script.Arabic, "شكرا 900", "شكراً")]
    [InlineData("ar", Script.Arabic, "أنا 900", "انا")]
    [InlineData("ar", Script.Arabic, "انا 900", "أنا")]
    [InlineData("ar", Script.Arabic, "كتاب 900", "كِتَاب")]
    [InlineData("uk", Script.Cyrillic, "п'ять 900", "пʼять")]
    [InlineData("uk", Script.Cyrillic, "п’ять 900", "п'ять")]
    public void The_list_and_the_query_fold_to_what_the_keys_type(string code, Script script, string entry, string query)
    {
        var path = TestDictionaries.List("fold", [entry, "xyz 1"]);
        var spell = new SpellChecker([new DictionarySource(code, path, script)]);

        Assert.True(spell.IsKnownWord(query, script));
        Assert.True(spell.IsKnownInLanguage(query, code));
        Assert.True(spell.Frequency(query, script) > 0.9);
    }

    [Fact]
    public void A_word_written_with_marks_is_kept_by_the_language_filter_not_thrown_away()
    {
        var path = TestDictionaries.List("tatweel", ["مــرحبا 900", "كِتَاب 800"]);
        var spell = new SpellChecker([new DictionarySource("ar", path, Script.Arabic)]);

        Assert.True(spell.IsKnownWord("مرحبا", Script.Arabic));
        Assert.True(spell.IsKnownWord("كتاب", Script.Arabic));
    }

    [Fact]
    public void A_vocalised_and_a_plain_spelling_become_one_entry_with_both_counts()
    {
        var path = TestDictionaries.List("merge", ["شكراً 600", "شكرا 400", "مرحبا 1000"]);
        var spell = new SpellChecker([new DictionarySource("ar", path, Script.Arabic)]);

        Assert.Equal(1.0, spell.FrequencyInLanguage("شكرا", "ar"), 3);
    }

    [Fact]
    public void An_apostrophe_word_is_known_through_its_parts_when_the_list_split_it()
    {
        var path = TestDictionaries.List("parts", ["я 9000", "здоров 400", "здорово 300", "ять 100"]);
        var spell = new SpellChecker([new DictionarySource("uk", path, Script.Cyrillic)]);

        Assert.True(spell.IsKnownWord("здоров'я", Script.Cyrillic));
        Assert.False(spell.IsKnownWord("здоровя", Script.Cyrillic));
        Assert.False(spell.IsKnownWord("п'ять", Script.Cyrillic));
        Assert.Equal(spell.Frequency("здоров", Script.Cyrillic), spell.Frequency("здоров'я", Script.Cyrillic), 6);
    }

    [Fact]
    public void A_word_of_the_neighbouring_language_is_not_this_language_s_word()
    {
        var path = TestDictionaries.List("alphabet", ["вітання 900", "это 800", "ты 700", "всё 600", "об'єкт 500", "the 400", "що 300"]);
        var uk = new SpellChecker([new DictionarySource("uk", path, Script.Cyrillic)]);

        Assert.True(uk.IsKnownWord("вітання", Script.Cyrillic));
        Assert.True(uk.IsKnownWord("об'єкт", Script.Cyrillic));
        Assert.True(uk.IsKnownWord("що", Script.Cyrillic));
        Assert.False(uk.IsKnownWord("это", Script.Cyrillic));
        Assert.False(uk.IsKnownWord("ты", Script.Cyrillic));
        Assert.False(uk.IsKnownWord("всё", Script.Cyrillic));
        Assert.False(uk.IsKnownWord("the", Script.Cyrillic));

        var ru = new SpellChecker([new DictionarySource("ru", path, Script.Cyrillic)]);
        Assert.True(ru.IsKnownWord("это", Script.Cyrillic));
        Assert.False(ru.IsKnownWord("об'єкт", Script.Cyrillic));
    }

    [Fact]
    public void A_language_written_in_another_script_keeps_its_list()
    {
        var path = TestDictionaries.List("sr", ["dobar 900", "dan 800"]);
        var spell = new SpellChecker([new DictionarySource("sr", path, Script.Latin)]);

        Assert.True(spell.IsKnownWord("dobar", Script.Latin));
    }

    [Fact]
    public void Latin_look_alikes_are_not_greek_words()
    {
        var path = TestDictionaries.List("el-mixed", ["να 900", "vα 800", "the 700", "καλός 600"]);
        var spell = new SpellChecker([new DictionarySource("el", path, Script.Greek)]);

        Assert.True(spell.IsKnownWord("να", Script.Greek));
        Assert.True(spell.IsKnownWord("καλός", Script.Greek));
        Assert.False(spell.IsKnownWord("vα", Script.Greek));
        Assert.False(spell.IsKnownWord("the", Script.Greek));
        Assert.False(spell.IsKnownWord("the", Script.Latin));
    }

    [Fact]
    public void Frequency_is_normalised_per_list_not_across_them()
    {
        var bigPath = TestDictionaries.List("big", ["cat 10000000", "moth 10"]);
        var smallPath = TestDictionaries.List("small", ["σκύλος 10000", "σκόρος 10"]);
        var spell = new SpellChecker(
        [
            new DictionarySource("en", bigPath, Script.Latin),
            new DictionarySource("el", smallPath, Script.Greek)
        ]);

        Assert.Equal(1.0, spell.FrequencyInLanguage("cat", "en"), 3);
        Assert.Equal(1.0, spell.FrequencyInLanguage("σκύλος", "el"), 3);

        Assert.True(spell.FrequencyInLanguage("moth", "en") < 0.2);
        Assert.True(spell.FrequencyInLanguage("σκόρος", "el") < 0.3);

        Assert.Equal(0.0, spell.Frequency("qqqq", Script.Latin));
    }
}
