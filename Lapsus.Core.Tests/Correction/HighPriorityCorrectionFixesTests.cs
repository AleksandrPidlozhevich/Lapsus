using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Correction;

public sealed class HighPriorityCorrectionFixesTests
{
    private readonly string _enPath;
    private readonly string _ukPath;
    private readonly LayoutCorrector _corrector;

    public HighPriorityCorrectionFixesTests()
    {
        _enPath = TestDictionaries.List("en", "cat 100", "car 80");
        _ukPath = TestDictionaries.List("uk", "кава 100", "молоко 90");
        var spell = new SpellChecker(
        [
            new DictionarySource("en", _enPath, Script.Latin),
            new DictionarySource("uk", _ukPath, Script.Cyrillic)
        ]);
        _corrector = new LayoutCorrector(spell);
    }

    [Fact]
    public void Null_target_installed_layout_still_used_as_candidate()
    {
        var customUk = BundledKeyboardMaps.Uk;
        var active = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En, "en-id");
        var installed = new List<LayoutSource>
        {
            active,
            new(Script.Cyrillic, null, customUk, "mystery-cyrillic-id")
        };
        var candidates = new List<LayoutCandidate>
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en-id"),
            new(Script.Cyrillic, null, customUk, null, "mystery-cyrillic-id")
        };

        var result = _corrector.CorrectPhrase("rfdf", active, installed, candidates);

        Assert.True(result.Changed);
        Assert.Equal("кава", result.Corrected);
        Assert.Equal("mystery-cyrillic-id", result.TargetLayoutId);
    }

    [Fact]
    public void Bundled_cyrillic_source_map_follows_installed_dictionary_not_ru_default()
    {
        var result = _corrector.CorrectPhrase("кава");
        Assert.False(result.Changed);
        Assert.Equal("кава", result.Corrected);
    }

    [Fact]
    public void Split_layout_id_follows_preferred_target()
    {
        var bePath = TestDictionaries.List("be", "кава 100");
        var spell = new SpellChecker(
        [
            new DictionarySource("en", _enPath, Script.Latin),
            new DictionarySource("uk", _ukPath, Script.Cyrillic),
            new DictionarySource("be", bePath, Script.Cyrillic)
        ]);
        var corrector = new LayoutCorrector(spell);

        var candidates = new List<LayoutCandidate>
        {
            new(Script.Cyrillic, KeyboardLayout.Be, BundledKeyboardMaps.Be, "be", "be-id"),
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk-id")
        };

        var result = corrector.CorrectPhrase(
            "rfdf", Script.Latin, BundledKeyboardMaps.En, candidates, KeyboardLayout.Uk);

        Assert.True(result.Changed);
        Assert.Equal("кава", result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
        Assert.Equal("uk-id", result.TargetLayoutId);
    }
}
