using System.Collections.Generic;
using System.IO;
using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using Xunit;

namespace Lapsus.Core.Tests.Languages.Latin;

public sealed class SameScriptLayoutTests : IDisposable
{
    private readonly string _enPath;
    private readonly string _dePath;
    private readonly LayoutCorrector _corrector;
    private readonly KeyboardMap _us = BundledKeyboardMaps.En;
    private readonly KeyboardMap _germanQwertz = TestKeyboardMaps.GermanQwertz;
    private readonly KeyboardMap _turkishQ = TestKeyboardMaps.TurkishQ;

    public SameScriptLayoutTests()
    {
        _enPath = WriteTemp("en", "hello 900", "don't 800", "cat 700");
        // "kazak" is US "kayak" on QWERTZ — in the dict so punctuation, not a miss, blocks the switch.
        _dePath = WriteTemp("de", "schön 900", "über 800", "größe 700", "fünf 650", "kazak 600", "şey 500");
        _corrector = new LayoutCorrector(new SpellChecker(
        [
            new DictionarySource("en", _enPath, Script.Latin),
            new DictionarySource("de", _dePath, Script.Latin)
        ]));
    }

    private List<LayoutCandidate> GermanCandidate =>
        [new(Script.Latin, null, _germanQwertz, "de", "de-id")];

    [Theory]
    [InlineData("sch;n", "schön")]
    [InlineData("[ber", "über")]
    [InlineData("f[nf", "fünf")]
    [InlineData("gr;-e", "größe")]
    public void German_typed_on_the_us_layout_is_recovered(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(typed, Script.Latin, _us, GermanCandidate, null, "en");

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal("de-id", result.TargetLayoutId);
    }

    [Fact]
    public void Turkish_typed_on_the_us_layout_is_recovered()
    {
        List<LayoutCandidate> turkish = [new(Script.Latin, null, _turkishQ, "de", "tr-id")];

        var result = _corrector.CorrectPhrase(";ey", Script.Latin, _us, turkish, null, "en");

        Assert.True(result.Changed);
        Assert.Equal("şey", result.Corrected);
        Assert.Equal("tr-id", result.TargetLayoutId);
    }

    [Fact]
    public void Letter_only_permutation_is_never_a_layout_switch()
    {
        var result = _corrector.CorrectPhrase("kayak", Script.Latin, _us, GermanCandidate, null, "en");

        Assert.Null(result.TargetLayout);
        Assert.Null(result.TargetLayoutId);
    }

    [Theory]
    [InlineData("don't")]
    [InlineData("sch;ne")]
    public void Same_script_switch_needs_a_real_word(string typed)
    {
        var result = _corrector.CorrectPhrase(typed, Script.Latin, _us, GermanCandidate, null, "en");

        Assert.False(result.Changed);
        Assert.Equal(typed, result.Corrected);
    }

    [Fact]
    public void Run_of_keys_that_carry_no_letter_is_not_a_word_of_its_own()
    {
        List<LayoutCandidate> cyrillic =
            [new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk-id")];

        var result = _corrector.CorrectPhrase("gr;-e", Script.Latin, _us, cyrillic, null, "en");

        Assert.False(result.Changed);
        Assert.Equal("gr;-e", result.Corrected);
    }

    [Theory]
    [InlineData("sch;n", true)]
    [InlineData("[ber", true)]
    [InlineData("gr;-e", true)]
    [InlineData("kayak", false)]
    [InlineData("hello", false)]
    public void Slip_signature_detects_a_letter_key_typed_as_punctuation(string text, bool expected)
    {
        Assert.Equal(expected, LayoutTranscoder.HasMistypedLetterKey(text, Script.Latin, _us, _germanQwertz));
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
        File.Delete(_dePath);
    }
}
