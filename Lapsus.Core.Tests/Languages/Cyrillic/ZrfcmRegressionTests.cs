using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Languages.Cyrillic;

public sealed class ZrfcmRegressionTests : IDisposable
{
    private readonly string _ukPath;
    private readonly LayoutCorrector _corrector;

    public ZrfcmRegressionTests()
    {
        _ukPath = WriteTempDictionary("uk", "якась 100");
        var spell = new SpellChecker(new[]
        {
            new DictionarySource("uk", _ukPath, Script.Cyrillic)
        });
        _corrector = new LayoutCorrector(spell);
    }

    [Fact]
    public void Latin_word_on_ukrainian_active_layout_becomes_yakas()
    {
        var active = new LayoutSource(Script.Cyrillic, "uk", BundledKeyboardMaps.Uk);
        var installed = new LayoutSource[]
        {
            new(Script.Latin, "en", BundledKeyboardMaps.En),
            new(Script.Latin, "en", BundledKeyboardMaps.En),
            active
        };
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en"),
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = _corrector.CorrectPhrase("zrfcm", active, installed, candidates, KeyboardLayout.Uk);

        Assert.True(result.Changed);
        Assert.Equal("якась", result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
    }

    [Fact]
    public void Latin_word_with_only_latin_candidates_uses_installed_cyrillic_source()
    {
        var active = new LayoutSource(Script.Cyrillic, "uk", BundledKeyboardMaps.Uk);
        var installed = new LayoutSource[]
        {
            new(Script.Latin, "en", BundledKeyboardMaps.En),
            active
        };
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en")
        };

        var result = _corrector.CorrectPhrase("zrfcm", active, installed, candidates);

        Assert.True(result.Changed);
        Assert.Equal("якась", result.Corrected);
    }

    [Fact]
    public void Latin_word_on_ukrainian_active_layout_with_uk_candidate_in_list()
    {
        var active = new LayoutSource(Script.Cyrillic, "uk", BundledKeyboardMaps.Uk);
        var installed = new LayoutSource[]
        {
            new(Script.Latin, "en", BundledKeyboardMaps.En),
            active
        };
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en"),
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = _corrector.CorrectPhrase("zrfcm", active, installed, candidates, KeyboardLayout.Uk);

        Assert.True(result.Changed);
        Assert.Equal("якась", result.Corrected);
    }

    [Fact]
    public void Glued_latin_words_without_space_split_and_correct_first_part()
    {
        var active = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En);
        var installed = new LayoutSource[] { active, new(Script.Cyrillic, "uk", BundledKeyboardMaps.Uk) };
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = _corrector.CorrectPhrase("zrfcmzedcm", active, installed, candidates, KeyboardLayout.Uk);

        Assert.True(result.Changed);
        Assert.StartsWith("якась", result.Corrected);
    }

    [Fact]
    public void Leading_comma_is_part_of_ukrainian_word()
    {
        var ukPath = WriteTempDictionary("uk", "брама 100");
        var spell = new SpellChecker(new[]
        {
            new DictionarySource("uk", ukPath, Script.Cyrillic)
        });
        var corrector = new LayoutCorrector(spell);
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = corrector.CorrectPhrase(",hfvf", Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.True(result.Changed);
        Assert.Equal("брама", result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
    }

    [Fact]
    public void Trailing_comma_stays_punctuation_after_english_word()
    {
        var enPath = WriteTempDictionary("en", "hello 100");
        var ukPath = WriteTempDictionary("uk", "брама 100");
        var spell = new SpellChecker(new[]
        {
            new DictionarySource("en", enPath, Script.Latin),
            new DictionarySource("uk", ukPath, Script.Cyrillic)
        });
        var corrector = new LayoutCorrector(spell);
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = corrector.CorrectPhrase("hello,", Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.False(result.Changed);
        Assert.Equal("hello,", result.Corrected);
    }

    [Fact]
    public void Two_latin_candidates_do_not_reencode_latin_to_latin()
    {
        var active = new LayoutSource(Script.Cyrillic, "uk", BundledKeyboardMaps.Uk);
        var installed = new LayoutSource[]
        {
            new(Script.Latin, "en", BundledKeyboardMaps.En),
            active
        };
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en"),
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = _corrector.CorrectPhrase("zrfcm", active, installed, candidates);

        Assert.NotEqual("o:0al", result.Corrected);
    }

    [Fact]
    public void Ukrainian_phrase_with_oem_commas_corrects_fully()
    {
        var ukPath = WriteTempDictionary("uk", "брама 100", "до 100", "неба 100");
        var spell = new SpellChecker(new[]
        {
            new DictionarySource("uk", ukPath, Script.Cyrillic)
        });
        var corrector = new LayoutCorrector(spell);
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = corrector.CorrectPhrase(",hfvf lj yt,f", Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.True(result.Changed);
        Assert.Equal("брама до неба", result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);

        File.Delete(ukPath);
    }

    [Fact]
    public void Ukrainian_phrase_corrects_short_word_without_dictionary_entry()
    {
        var ukPath = WriteTempDictionary("uk", "брама 100", "неба 100");
        var spell = new SpellChecker(new[]
        {
            new DictionarySource("uk", ukPath, Script.Cyrillic)
        });
        var corrector = new LayoutCorrector(spell);
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = corrector.CorrectPhrase(",hfvf lj yt,f", Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.True(result.Changed);
        Assert.Equal("брама до неба", result.Corrected);

        File.Delete(ukPath);
    }

    [Fact]
    public void Phrase_with_leading_bracket_letter_is_i_want()
    {
        var ukPath = WriteTempDictionary("uk", "я 100", "хочу 100");
        var spell = new SpellChecker(new[]
        {
            new DictionarySource("uk", ukPath, Script.Cyrillic)
        });
        var corrector = new LayoutCorrector(spell);
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = corrector.CorrectPhrase("z [jxe", Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.True(result.Changed);
        Assert.Equal("я хочу", result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);

        File.Delete(ukPath);
    }

    [Theory]
    [InlineData("z", "я")]
    [InlineData("rfr", "как")]
    [InlineData("ij", "шо")]
    [InlineData("lt", "де")]
    [InlineData("[jxe", "хочу")]
    public void Short_wrong_layout_words_correct_when_known(string input, string expected)
    {
        var ukPath = WriteTempDictionary("uk", "я 100", "как 100", "шо 100", "де 100", "хочу 100");
        var spell = new SpellChecker(new[]
        {
            new DictionarySource("uk", ukPath, Script.Cyrillic)
        });
        var corrector = new LayoutCorrector(spell);
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = corrector.CorrectPhrase(input, Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);

        File.Delete(ukPath);
    }

    [Fact]
    public void Interior_comma_is_part_of_wrong_layout_word()
    {
        var ukPath = WriteTempDictionary("uk", "неба 100");
        var spell = new SpellChecker(new[]
        {
            new DictionarySource("uk", ukPath, Script.Cyrillic)
        });
        var corrector = new LayoutCorrector(spell);
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk")
        };

        var result = corrector.CorrectPhrase("yt,f", Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.True(result.Changed);
        Assert.Equal("неба", result.Corrected);

        File.Delete(ukPath);
    }

    [Fact]
    public void Picks_installed_layout_variant_with_best_transcode_score()
    {
        var ukPath = WriteTempDictionary("uk", "брама 100");
        var spell = new SpellChecker(new[]
        {
            new DictionarySource("uk", ukPath, Script.Cyrillic)
        });
        var corrector = new LayoutCorrector(spell);

        Assert.True(BundledKeyboardMaps.En.TryGetSlot(',', out var commaSlot));
        var brokenSlots = new char[48];
        for (var i = 0; i < brokenSlots.Length; i++)
            brokenSlots[i] = BundledKeyboardMaps.Uk.CharAtSlot(i);
        brokenSlots[commaSlot] = '-';

        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, new KeyboardMap(brokenSlots), "uk", "com.apple.Ukrainian-broken"),
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "com.apple.Ukrainian-Legacy")
        };

        var result = corrector.CorrectPhrase(",hfvf", Script.Latin, BundledKeyboardMaps.En, candidates);

        Assert.True(result.Changed);
        Assert.Equal("брама", result.Corrected);
        Assert.Equal("com.apple.Ukrainian-Legacy", result.TargetLayoutId);

        File.Delete(ukPath);
    }

    private static string WriteTempDictionary(string tag, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{tag}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    public void Dispose()
    {
        File.Delete(_ukPath);
    }
}
