using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

public sealed class PhraseContextTests : IDisposable
{
    private readonly string _enPath;
    private readonly string _ruPath;
    private readonly LayoutCorrector _corrector;

    private static readonly LayoutSource EnSource =
        new(Script.Latin, "en", BundledKeyboardMaps.En, "en-id");

    private static readonly LayoutSource RuSource =
        new(Script.Cyrillic, "ru", BundledKeyboardMaps.Ru, "ru-id");

    private static readonly LayoutSource KaSource =
        new(Script.Georgian, "ka", BundledKeyboardMaps.Ka, "ka-id");

    private static readonly List<LayoutSource> Installed = [EnSource, RuSource];

    private static readonly List<LayoutCandidate> Candidates =
    [
        new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en-id"),
        new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru", "ru-id")
    ];

    public PhraseContextTests()
    {
        _enPath = WriteTemp("en",
            "her 90000", "world 5000", "hello 900", "text 900", "milk 700", "car 600", "cat 500",
            "let 900000", "vjkjj 900000", "z 40", "d 40", "b 30");

        _ruPath = WriteTemp("ru",
            "в 9000", "и 8000", "я 5000", "привет 4000", "мир 3500", "хочу 3000", "уже 2500", "текст 2000",
            "ж 1500", "кофе 1000", "молоко 800", "дуже 800", "тень 500", "рука 400", "рук 300");

        var spell = new SpellChecker(
        [
            new DictionarySource("en", _enPath, Script.Latin),
            new DictionarySource("ru", _ruPath, Script.Cyrillic)
        ]);
        _corrector = new LayoutCorrector(spell);
    }

    private PhraseCorrection Correct(string text, LayoutSource? active = null,
        IReadOnlyList<LayoutSource>? installed = null, CorrectionHints hints = default)
    {
        return _corrector.CorrectPhrase(text, active ?? EnSource, installed ?? Installed, Candidates, null, hints);
    }

    [Fact]
    public void A_known_line_direction_lets_one_crossed_word_pull_a_straggler_in()
    {
        Assert.Equal("привет d", Correct("ghbdtn d").Corrected);

        var result = Correct("ghbdtn d", hints: new CorrectionHints(LineDirection: Script.Cyrillic));

        Assert.Equal("привет в", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    [Fact]
    public void A_known_line_direction_pulls_a_lone_short_word_the_dictionary_knows()
    {
        var result = Correct("d", hints: new CorrectionHints(LineDirection: Script.Cyrillic));

        Assert.Equal("в", result.Corrected);
    }

    [Fact]
    public void A_known_line_direction_does_not_pull_a_word_the_dictionary_does_not_know()
    {
        var result = Correct("ghbdtn car", hints: new CorrectionHints(LineDirection: Script.Cyrillic));

        Assert.Equal("привет car", result.Corrected);
    }

    [Fact]
    public void A_known_line_direction_yields_to_a_crossing_the_other_way()
    {
        var result = Correct("сфк d", active: RuSource, hints: new CorrectionHints(LineDirection: Script.Cyrillic));

        Assert.Equal("car d", result.Corrected);
    }

    [Fact]
    public void A_typo_the_english_line_outvotes_is_fixed_in_place_instead_of_crossing()
    {
        Assert.Equal("уже", Correct("ext").Corrected);

        var result = Correct("hello world ext");

        Assert.Equal("hello world text", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Fact]
    public void A_keys_alone_collision_inside_an_english_line_is_a_typo_too()
    {
        Assert.Equal("рука", Correct("herf").Corrected);

        Assert.Equal("hello world her", Correct("hello world herf").Corrected);
    }

    [Fact]
    public void A_dropped_letter_is_restored_when_the_other_layout_reads_a_commoner_word()
    {
        // "молоо" is молоко with к dropped; those keys alone are the common English word "vjkjj".
        var result = Correct("привет мир молоо", active: RuSource);

        Assert.Equal("привет мир молоко", result.Corrected);
    }

    [Fact]
    public void A_swapped_pair_is_restored_when_the_other_layout_reads_a_commoner_word()
    {
        // "дуеж" is дуже with е/ж swapped; those keys alone are the common English word "let".
        var result = Correct("привет мир дуеж", active: RuSource);

        Assert.Equal("привет мир дуже", result.Corrected);
    }

    [Fact]
    public void Keys_only_leaves_the_outvoted_typo_as_typed()
    {
        var result = Correct("hello world ext", hints: new CorrectionHints(KeysOnly: true));

        Assert.Equal("hello world ext", result.Corrected);
    }

    [Fact]
    public void A_wrong_layout_word_with_no_english_fix_still_crosses_inside_an_english_line()
    {
        Assert.Equal("hello world привет", Correct("hello world ghbdtn").Corrected);
    }

    [Fact]
    public void One_anchor_is_not_a_line()
    {
        Assert.Equal("hello уже", Correct("hello ext").Corrected);
    }

    [Fact]
    public void A_second_crossing_the_same_way_corroborates_the_first()
    {
        Assert.Equal("hello world уже привет", Correct("hello world ext ghbdtn").Corrected);
    }

    [Fact]
    public void A_key_that_is_a_letter_elsewhere_follows_a_settled_line()
    {
        Assert.Equal("хочу кофе ж", Correct("[jxe rjat ;").Corrected);
    }

    [Fact]
    public void The_same_key_on_an_english_line_stays_punctuation()
    {
        Assert.Equal("hello world ;", Correct("hello world ;").Corrected);
        Assert.Equal(";", Correct(";").Corrected);
    }

    [Fact]
    public void A_known_line_direction_pulls_the_key_alone()
    {
        Assert.Equal("ж", Correct(";", hints: new CorrectionHints(LineDirection: Script.Cyrillic)).Corrected);
    }

    [Theory]
    [InlineData("ghbdtn, vbh", "привет, мир")]
    [InlineData("ghbdtn. vbh", "привет. мир")]
    [InlineData("ghbdtn, vbh, ntrcn", "привет, мир, текст")]
    public void Punctuation_glued_to_a_switched_word_is_not_a_lone_letter_key(string typed, string expected)
    {
        Assert.Equal(expected, Correct(typed).Corrected);
    }

    [Fact]
    public void A_function_word_the_english_list_also_knows_is_pulled_into_the_settled_line()
    {
        var result = Correct("z [jxe rjat b ntrcn");

        Assert.True(result.Changed);
        Assert.Equal("я хочу кофе и текст", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    [Fact]
    public void A_straggler_needs_two_other_words_to_agree()
    {
        var result = Correct("d ntrcn");

        Assert.True(result.Changed);
        Assert.Equal("d текст", result.Corrected);
    }

    [Fact]
    public void A_commoner_english_word_is_not_pulled_in()
    {
        var result = Correct("her rjat ntrcn");

        Assert.True(result.Changed);
        Assert.Equal("her кофе текст", result.Corrected);
    }

    [Fact]
    public void A_word_on_the_leave_alone_list_is_not_pulled_in_either()
    {
        var corrector = new LayoutCorrector(
            new SpellChecker(
            [
                new DictionarySource("en", _enPath, Script.Latin),
                new DictionarySource("ru", _ruPath, Script.Cyrillic)
            ]),
            exceptions: new WordExceptions(["z"]));

        var result = corrector.CorrectPhrase("z [jxe rjat b ntrcn", EnSource, Installed, Candidates);

        Assert.Equal("z хочу кофе и текст", result.Corrected);
    }

    [Fact]
    public void A_scoring_blind_source_with_no_dictionary_is_not_pulled_in_either()
    {
        var installed = new List<LayoutSource> { EnSource, RuSource, KaSource };
        var candidates = new List<LayoutCandidate>(Candidates)
        {
            new(Script.Georgian, KeyboardLayout.Ka, BundledKeyboardMaps.Ka, "ka", "ka-id")
        };

        var result = _corrector.CorrectPhrase("сфк ьшдл ცატ", RuSource, installed, candidates);

        Assert.True(result.Changed);
        Assert.Equal("car milk ცატ", result.Corrected);
    }

    [Fact]
    public void A_foreign_word_inside_a_settled_line_is_not_spell_fixed_across_scripts()
    {
        var result = Correct("привет мир npm");

        Assert.False(result.Changed);
        Assert.Equal("привет мир npm", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Fact]
    public void A_two_edit_guess_beside_english_words_is_not_carried_by_crossings_elsewhere()
    {
        var result = Correct("ghbdtn vbh hello world npm");

        Assert.Equal("привет мир hello world npm", result.Corrected);
    }

    [Fact]
    public void The_same_foreign_word_alone_is_still_corrected()
    {
        var result = Correct("npm");

        Assert.True(result.Changed);
        Assert.Equal("тень", result.Corrected);
    }

    [Fact]
    public void A_wrong_layout_word_beside_a_real_one_is_still_corrected()
    {
        var result = _corrector.CorrectPhrase("молоко сфе", RuSource, Installed, Candidates);

        Assert.True(result.Changed);
        Assert.Equal("молоко cat", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void A_word_that_crossed_on_the_keys_alone_corroborates_the_odd_one_out()
    {
        var result = Correct("ntrcn rjat npm");

        Assert.True(result.Changed);
        Assert.Equal("текст кофе тень", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    [Fact]
    public void A_word_left_as_typed_keeps_its_own_layout_out_of_the_vote()
    {
        var result = Correct("ntrcn hello world");

        Assert.True(result.Changed);
        Assert.Equal("текст hello world", result.Corrected);
        Assert.Null(result.TargetLayout);
    }

    [Fact]
    public void A_counter_vote_cannot_outvote_the_words_that_really_switched()
    {
        var result = Correct("hello ntrcn rjat");

        Assert.True(result.Changed);
        Assert.Equal("hello текст кофе", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    private static string WriteTemp(string tag, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{tag}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    public void Dispose()
    {
        File.Delete(_enPath);
        File.Delete(_ruPath);
    }
}
