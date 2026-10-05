using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

public sealed class TrailingPunctuationTests : IDisposable
{
    private readonly string _arPath;
    private readonly string _enPath;
    private readonly string _ruPath;
    private readonly LayoutCorrector _corrector;

    private static readonly List<LayoutCandidate> Installed =
    [
        new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en-id"),
        new(Script.Arabic, KeyboardLayout.Ar, BundledKeyboardMaps.Ar, "ar", "ar-id"),
        new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru", "ru-id")
    ];

    public TrailingPunctuationTests()
    {
        _arPath = WriteTemp("ar", "بعد 900", "بك 800", "ذلك 700", "فقط 600", "مرحبا 500", "بع 400", "ذل 300");
        // простою is rare beside просто, matching real list proportions.
        _ruPath = WriteTemp("ru", "знаю 900", "просто 800", "простою 1", "привет 900", "как 900", "дела 800",
            "фею 300", "их 900", "мою 800");
        _enPath = WriteTemp("en", "hello 900", "text 800", "guy 700", "fu 600", "big 500", "what 900", "at 900",
            "b 400", "vj 5");

        _corrector = new LayoutCorrector(new SpellChecker(
        [
            new DictionarySource("ar", _arPath, Script.Arabic),
            new DictionarySource("ru", _ruPath, Script.Cyrillic),
            new DictionarySource("en", _enPath, Script.Latin)
        ]));
    }

    public void Dispose()
    {
        File.Delete(_arPath);
        File.Delete(_ruPath);
        File.Delete(_enPath);
    }

    [Theory]
    [InlineData("fu]", "بعد")]
    [InlineData("f;", "بك")]
    [InlineData("`g;", "ذلك")]
    [InlineData("tr'", "فقط")]
    [InlineData("pyf.", "знаю")]
    [InlineData("b[", "их")] // One-letter "b" is too short to own "[" (х).
    [InlineData("vj.", "мою")] // Rare "vj" loses its full stop to commoner "мою".
    public void Last_letter_on_a_punctuation_key_is_recovered(string typed, string expected)
    {
        var result = Correct(typed);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
    }

    [Fact]
    public void Recovered_word_is_part_of_the_phrase()
    {
        var result = Correct("lvpfh f;");

        Assert.Equal("مرحبا بك", result.Corrected);
        Assert.Equal(KeyboardLayout.Ar, result.TargetLayout);
    }

    [Theory]
    [InlineData("hello,")]
    [InlineData("text;")]
    [InlineData("guy;")]
    [InlineData("big;")]
    [InlineData("at.")] // Common "at." keeps its stop against rarer "фею".
    public void A_real_word_keeps_its_punctuation(string typed)
    {
        var result = Correct(typed);

        Assert.False(result.Changed);
        Assert.Equal(typed, result.Corrected);
    }

    [Fact]
    public void A_full_stop_after_a_switched_word_stays_a_full_stop()
    {
        // "простою" (ю on ".") must not beat "просто."
        var result = Correct("ghjcnj.");

        Assert.True(result.Changed);
        Assert.Equal("просто.", result.Corrected);
    }

    [Fact]
    public void Punctuation_after_a_recovered_word_survives()
    {
        var result = Correct("lvpfh.");

        Assert.True(result.Changed);
        Assert.Equal("مرحبا.", result.Corrected);
    }

    [Fact]
    public void A_key_the_target_layout_cannot_type_blocks_the_reading()
    {
        // "b" is the empty Arabic lam-alef slot; must not become "bهلك".
        var result = Correct("big;");

        Assert.False(result.Changed);
        Assert.Equal("big;", result.Corrected);
    }

    [Theory]
    [InlineData("ghbdtn? rfr ltkf/", "привет, как дела.")]
    [InlineData("ghbdtn&", "привет?")]
    [InlineData("ghbdtn/rfr", "привет.как")]
    public void Punctuation_typed_with_a_switched_word_is_retyped_with_it(string typed, string expected)
    {
        var result = CorrectOnOsMaps(typed, Script.Latin, OsEn, "en");

        Assert.Equal(expected, result.Corrected);
    }

    [Theory]
    [InlineData("hello, rfr", "hello, как")]
    [InlineData("ghbdtn/hello", "привет/hello")]
    [InlineData("ltkf.", "дела.")]
    public void Punctuation_not_owned_by_a_switched_word_stays(string typed, string expected)
    {
        var result = CorrectOnOsMaps(typed, Script.Latin, OsEn, "en");

        Assert.Equal(expected, result.Corrected);
    }

    [Fact]
    public void Punctuation_after_a_word_typed_on_russian_keys_is_retyped_too()
    {
        var result = CorrectOnOsMaps("цреф,", Script.Cyrillic, OsRu, "ru");

        Assert.Equal("what?", result.Corrected);
    }

    [Fact]
    public void A_cased_layout_declares_only_the_symbols_over_non_letter_keys()
    {
        Assert.True(OsEn.TryGetKey('?', out var slot, out var shift));
        Assert.Equal(KeyShift.Layer, shift);
        Assert.Equal(',', OsRu.CharAtSlot(slot, shift));

        Assert.True(OsEn.TryGetKey('A', out slot, out shift));
        Assert.Equal('Ф', OsRu.CharAtSlot(slot, shift));
    }

    // Full base row; shifted symbols only over non-letter keys, as OS backends build them.
    private static readonly KeyboardMap OsEn = OsMap(BundledKeyboardMaps.CreateEnSlots(), [('/', '?'), ('7', '&')]);

    private static readonly KeyboardMap OsRu = OsMap(
        BundledKeyboardMaps.CreateRuSlots(), [('/', ','), ('7', '?')], ('/', '.'));

    private static KeyboardMap OsMap(char[] slots, (char UsKey, char Shifted)[] shiftOver, (char UsKey, char Base)? baseOver = null)
    {
        var shifted = new char[slots.Length];
        foreach (var (usKey, symbol) in shiftOver)
        {
            BundledKeyboardMaps.En.TryGetSlot(usKey, out var slot);
            shifted[slot] = symbol;
        }

        if (baseOver is var (key, replacement))
        {
            BundledKeyboardMaps.En.TryGetSlot(key, out var slot);
            slots[slot] = replacement;
        }

        for (var letter = 0; letter < 26; letter++)
            shifted[letter] = char.ToUpperInvariant(slots[letter]);

        return new KeyboardMap(slots, KeyboardMap.ShiftedSymbolsOnly(slots, shifted));
    }

    private PhraseCorrection CorrectOnOsMaps(string typed, Script script, KeyboardMap map, string language)
    {
        List<LayoutCandidate> installed =
        [
            new(Script.Latin, KeyboardLayout.En, OsEn, "en", "en-id"),
            new(Script.Cyrillic, KeyboardLayout.Ru, OsRu, "ru", "ru-id")
        ];

        return _corrector.CorrectPhrase(typed, script, map, installed, null, language);
    }

    private PhraseCorrection Correct(string typed)
    {
        return _corrector.CorrectPhrase(typed, Script.Latin, BundledKeyboardMaps.En, Installed, null, "en");
    }

    private static string WriteTemp(string tag, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{tag}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }
}
