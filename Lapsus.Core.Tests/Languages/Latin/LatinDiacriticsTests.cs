using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Languages.Latin;

public sealed class LatinDiacriticsTests : IDisposable
{
    private readonly string _latinPath;
    private readonly string _ukPath;
    private readonly LayoutCorrector _corrector;
    private readonly KeyboardMap _turkishQ = TestKeyboardMaps.TurkishQ;

    private static readonly List<LayoutCandidate> ToUkrainian =
        [new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk-id")];

    public LatinDiacriticsTests()
    {
        _latinPath = WriteTemp(
            "tr",
            "şey 900", "ağaç 800", "çocuk 700", "değil 600",
            "schön 500", "über 400", "łódź 300", "tiếng 200");
        _ukPath = WriteTemp("uk", "хата 900", "кава 800");
        _corrector = new LayoutCorrector(new SpellChecker(
        [
            new DictionarySource("tr", _latinPath, Script.Latin),
            new DictionarySource("uk", _ukPath, Script.Cyrillic)
        ]));
    }

    [Theory]
    [InlineData('ş', Script.Latin)]
    [InlineData('ğ', Script.Latin)]
    [InlineData('ı', Script.Latin)]
    [InlineData('ö', Script.Latin)]
    [InlineData('ł', Script.Latin)]
    [InlineData('ế', Script.Latin)]
    [InlineData('ß', Script.Latin)]
    public void Non_ascii_latin_letters_are_latin(char ch, Script expected)
    {
        Assert.Equal(expected, Scripts.Of(ch));
    }

    [Theory]
    [InlineData('×')]
    [InlineData('÷')]
    public void Maths_signs_in_the_latin_block_are_not_letters(char ch)
    {
        Assert.Null(Scripts.Of(ch));
    }

    [Theory]
    [InlineData("şey")]
    [InlineData("ağaç")]
    [InlineData("çocuk")]
    [InlineData("değil")]
    [InlineData("schön")]
    [InlineData("über")]
    [InlineData("łódź")]
    [InlineData("tiếng")]
    public void Known_word_with_diacritics_survives(string word)
    {
        var result = _corrector.CorrectPhrase(word, Script.Latin, _turkishQ, ToUkrainian, null, "tr");

        Assert.False(result.Changed);
        Assert.Equal(word, result.Corrected);
    }

    [Theory]
    [InlineData("ğfnf", "хата")]
    [InlineData("rfdf", "кава")]
    public void Turkish_keystrokes_still_decode_to_ukrainian(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(typed, Script.Latin, _turkishQ, ToUkrainian, null, "tr");

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
    }

    private static string WriteTemp(string tag, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{tag}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    public void Dispose()
    {
        File.Delete(_latinPath);
        File.Delete(_ukPath);
    }
}
