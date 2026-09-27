using System.Collections.Generic;
using System.IO;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

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
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-uk-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "привіт 500\nдякую 300\nмісто 200\n");
        try
        {
            var spell = new SpellChecker(new[] { new DictionarySource("uk", path, Script.Cyrillic) });

            Assert.True(spell.Has(Script.Cyrillic));
            Assert.True(spell.IsKnownWord("привіт", Script.Cyrillic));
            Assert.True(spell.TryCorrect("дяку", Script.Cyrillic, out var fixedWord, out _));
            Assert.Equal("дякую", fixedWord);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_list_big_enough_teaches_a_trigram_model_and_a_tiny_one_does_not()
    {
        var ruPath = Path.Combine(Path.GetTempPath(), $"lapsus-ngram-{Guid.NewGuid():N}.txt");
        var tinyPath = Path.Combine(Path.GetTempPath(), $"lapsus-tiny-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(ruPath, SampleWords.RussianLines());
        File.WriteAllLines(tinyPath, ["cat 10", "dog 5"]);
        try
        {
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
        finally
        {
            File.Delete(ruPath);
            File.Delete(tinyPath);
        }
    }

    [Fact]
    public void Pools_a_greek_dictionary_under_the_greek_script()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-el-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "καλημέρα 500\nευχαριστώ 300\nνερό 200\n");
        try
        {
            var spell = new SpellChecker(new[] { new DictionarySource("el", path, Script.Greek) });

            Assert.True(spell.Has(Script.Greek));
            Assert.True(spell.IsKnownWord("νερό", Script.Greek));
            Assert.False(spell.IsKnownWord("νερό", Script.Latin));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Pools_dictionaries_by_script_across_languages()
    {
        var ukPath = Path.Combine(Path.GetTempPath(), $"lapsus-uk-pool-{Guid.NewGuid():N}.txt");
        var ruPath = Path.Combine(Path.GetTempPath(), $"lapsus-ru-pool-{Guid.NewGuid():N}.txt");
        File.WriteAllText(ukPath, "привіт 500\n");
        File.WriteAllText(ruPath, "привет 500\n");
        try
        {
            var spell = new SpellChecker(
            [
                new DictionarySource("uk", ukPath, Script.Cyrillic),
                new DictionarySource("ru", ruPath, Script.Cyrillic)
            ]);

            Assert.True(spell.IsKnownWord("привет", Script.Cyrillic, "uk"));
            Assert.True(spell.IsKnownWord("привіт", Script.Cyrillic, "ru"));
            Assert.True(spell.IsKnownWord("привет", Script.Cyrillic));
            Assert.True(spell.IsKnownWord("привіт", Script.Cyrillic));
        }
        finally
        {
            File.Delete(ukPath);
            File.Delete(ruPath);
        }
    }
    [Fact]
    public void A_correction_prefers_the_commoner_word_between_two_dictionaries_of_one_script()
    {
        var ruPath = Path.Combine(Path.GetTempPath(), $"lapsus-ru-freq-{Guid.NewGuid():N}.txt");
        var ukPath = Path.Combine(Path.GetTempPath(), $"lapsus-uk-freq-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(ruPath, ["кит 50", "дом 100000"]);
        File.WriteAllLines(ukPath, ["кіт 9000", "хата 10000"]);
        try
        {
            var spell = new SpellChecker(
            [
                new DictionarySource("ru", ruPath, Script.Cyrillic),
                new DictionarySource("uk", ukPath, Script.Cyrillic)
            ]);

            Assert.True(spell.TryCorrect("кот", Script.Cyrillic, out var corrected, out var distance));
            Assert.Equal("кіт", corrected);
            Assert.Equal(1, distance);
        }
        finally
        {
            File.Delete(ruPath);
            File.Delete(ukPath);
        }
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
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-fold-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, [entry, "xyz 1"]);
        try
        {
            var spell = new SpellChecker([new DictionarySource(code, path, script)]);

            Assert.True(spell.IsKnownWord(query, script));
            Assert.True(spell.IsKnownInLanguage(query, code));
            Assert.True(spell.Frequency(query, script) > 0.9);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_word_written_with_marks_is_kept_by_the_language_filter_not_thrown_away()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-tatweel-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, ["مــرحبا 900", "كِتَاب 800"]);
        try
        {
            var spell = new SpellChecker([new DictionarySource("ar", path, Script.Arabic)]);

            Assert.True(spell.IsKnownWord("مرحبا", Script.Arabic));
            Assert.True(spell.IsKnownWord("كتاب", Script.Arabic));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_vocalised_and_a_plain_spelling_become_one_entry_with_both_counts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-merge-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, ["شكراً 600", "شكرا 400", "مرحبا 1000"]);
        try
        {
            var spell = new SpellChecker([new DictionarySource("ar", path, Script.Arabic)]);

            Assert.Equal(1.0, spell.FrequencyInLanguage("شكرا", "ar"), 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_apostrophe_word_is_known_through_its_parts_when_the_list_split_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-parts-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, ["я 9000", "здоров 400", "здорово 300", "ять 100"]);
        try
        {
            var spell = new SpellChecker([new DictionarySource("uk", path, Script.Cyrillic)]);

            Assert.True(spell.IsKnownWord("здоров'я", Script.Cyrillic));
            Assert.False(spell.IsKnownWord("здоровя", Script.Cyrillic));
            Assert.False(spell.IsKnownWord("п'ять", Script.Cyrillic));
            Assert.Equal(spell.Frequency("здоров", Script.Cyrillic), spell.Frequency("здоров'я", Script.Cyrillic), 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_word_of_the_neighbouring_language_is_not_this_language_s_word()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-alphabet-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, ["привіт 900", "это 800", "ты 700", "всё 600", "об'єкт 500", "the 400", "що 300"]);
        try
        {
            var uk = new SpellChecker([new DictionarySource("uk", path, Script.Cyrillic)]);

            Assert.True(uk.IsKnownWord("привіт", Script.Cyrillic));
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
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_language_written_in_another_script_keeps_its_list()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-sr-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, ["dobar 900", "dan 800"]);
        try
        {
            var spell = new SpellChecker([new DictionarySource("sr", path, Script.Latin)]);

            Assert.True(spell.IsKnownWord("dobar", Script.Latin));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Latin_look_alikes_are_not_greek_words()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-el-mixed-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, ["να 900", "vα 800", "the 700", "καλός 600"]);
        try
        {
            var spell = new SpellChecker([new DictionarySource("el", path, Script.Greek)]);

            Assert.True(spell.IsKnownWord("να", Script.Greek));
            Assert.True(spell.IsKnownWord("καλός", Script.Greek));
            Assert.False(spell.IsKnownWord("vα", Script.Greek));
            Assert.False(spell.IsKnownWord("the", Script.Greek));
            Assert.False(spell.IsKnownWord("the", Script.Latin));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Frequency_is_normalised_per_list_not_across_them()
    {
        var bigPath = Path.Combine(Path.GetTempPath(), $"lapsus-big-{Guid.NewGuid():N}.txt");
        var smallPath = Path.Combine(Path.GetTempPath(), $"lapsus-small-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(bigPath, ["cat 10000000", "moth 10"]);
        File.WriteAllLines(smallPath, ["σκύλος 10000", "σκόρος 10"]);
        try
        {
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
        finally
        {
            File.Delete(bigPath);
            File.Delete(smallPath);
        }
    }

}
