using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Languages.Korean;

// Keys are the two-set (dubeolsik) layout: dkssud is 안녕, and what the keys type gathers into syllables.
public sealed class KoreanCorrectionTests
{
    private static readonly string[] KoreanWords =
    [
        "안녕 900", "한글 800", "있어요 700", "값이 600", "꽃 500", "아빠 400", "학교에 300", "고마워요 200"
    ];

    private static readonly string[] EnglishWords = ["hello 900", "text 800", "world 700", "windows 600", "test 500"];

    private static readonly List<LayoutCandidate> ToKorean =
        [new(Script.Hangul, KeyboardLayout.Ko, BundledKeyboardMaps.Ko, "ko")];

    private static readonly List<LayoutCandidate> ToLatin =
        [new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en")];

    private readonly LayoutCorrector _corrector = new(new SpellChecker(
    [
        new DictionarySource("ko", TestDictionaries.List("ko", KoreanWords), Script.Hangul),
        new DictionarySource("en", TestDictionaries.List("en", EnglishWords), Script.Latin)
    ]));

    [Theory]
    [InlineData("dkssud", "안녕")]
    [InlineData("gksrmf", "한글")]
    [InlineData("Rhc", "꽃")]
    [InlineData("dkQk", "아빠")]
    [InlineData("gkrrydp", "학교에")]
    [InlineData("rhakdnjdy", "고마워요")]
    public void Latin_keystrokes_switch_to_korean(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(typed, Script.Latin, BundledKeyboardMaps.En, ToKorean);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.Ko, result.TargetLayout);
    }

    [Fact]
    public void A_whole_line_typed_on_the_english_layout_is_read_as_korean()
    {
        var result = _corrector.CorrectPhrase("dkssud, rkqtdl dlTdjdy", Script.Latin, BundledKeyboardMaps.En, ToKorean);

        Assert.Equal("안녕, 값이 있어요", result.Corrected);
    }

    [Fact]
    public void A_korean_word_the_list_lacks_is_never_respelled_into_one_it_has()
    {
        // 학교가 is one syllable from the listed 학교에.
        var alone = _corrector.CorrectPhrase("gkrryrk", Script.Latin, BundledKeyboardMaps.En, ToKorean);
        Assert.NotEqual("학교에", alone.Corrected);

        var inALine = _corrector.CorrectPhrase("dkssud gkrryrk dlTdjdy", Script.Latin, BundledKeyboardMaps.En, ToKorean);
        Assert.DoesNotContain("학교에", inALine.Corrected);
    }

    [Theory]
    [InlineData("ㅗ디ㅣㅐ", "hello")]
    [InlineData("쟈ㅜ앶ㄴ", "windows")]
    [InlineData("ㅅㄷㄴㅅ", "test")]
    [InlineData("ㅅㄷㅌㅅ", "text")]
    public void Korean_keystrokes_switch_to_english(string typed, string expected)
    {
        var result = _corrector.CorrectPhrase(typed, Script.Hangul, BundledKeyboardMaps.Ko, ToLatin);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("world")]
    [InlineData("zzzqqq")] // gathers into syllables like any letter keys, but into no word
    public void English_the_list_knows_or_korean_it_does_not_is_left_alone(string typed)
    {
        var result = _corrector.CorrectPhrase(typed, Script.Latin, BundledKeyboardMaps.En, ToKorean);

        Assert.False(result.Changed);
        Assert.Equal(typed, result.Corrected);
    }

    [Theory]
    [InlineData("안녕")]
    [InlineData("있어요")]
    [InlineData("ㅋㅋㅋ")]
    public void Korean_is_left_alone(string typed)
    {
        var result = _corrector.CorrectPhrase(typed, Script.Hangul, BundledKeyboardMaps.Ko, ToLatin);

        Assert.False(result.Changed);
        Assert.Equal(typed, result.Corrected);
    }
}
