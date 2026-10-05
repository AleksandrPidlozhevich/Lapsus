using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

public sealed class SplitCorrectionTests : IDisposable
{
    private readonly string _enPath;
    private readonly string _ukPath;
    private readonly LayoutCorrector _corrector;

    public SplitCorrectionTests()
    {
        _enPath = WriteTemp("en", "car 5000", "carrot 900", "carrots 100", "rot 400", "cat 800", "audio 700");
        _ukPath = WriteTemp("uk", "кава 100", "молоко 90");
        _corrector = new LayoutCorrector(new SpellChecker(
        [
            new DictionarySource("en", _enPath, Script.Latin),
            new DictionarySource("uk", _ukPath, Script.Cyrillic)
        ]));
    }

    [Theory]
    [InlineData("carrots")]
    [InlineData("carrots and cat")]
    public void Known_word_is_never_split(string input)
    {
        var result = _corrector.CorrectPhrase(input);

        Assert.False(result.Changed);
        Assert.Equal(input, result.Corrected);
    }

    [Fact]
    public void Run_together_halves_are_still_corrected()
    {
        var result = _corrector.CorrectPhrase("audiorfdf");

        Assert.True(result.Changed);
        Assert.Equal("audioкава", result.Corrected);
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
        File.Delete(_ukPath);
    }
}
