using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests.Layout;

public class HangulTests
{
    [Theory]
    [InlineData("ㅇㅏㄴㄴㅕㅇ", "안녕")]
    [InlineData("ㅎㅏㄴㄱㅡㄹ", "한글")]
    [InlineData("ㄱㅏㅂㅅ", "값")]
    [InlineData("ㄱㅏㅂㅅㅣ", "갑시")]
    [InlineData("ㄱㅏㅂㅅㅇㅣ", "값이")]
    [InlineData("ㄱㅗㅏ", "과")]
    [InlineData("ㅇㅡㅣㅅㅏ", "의사")]
    [InlineData("ㄲㅗㅊ", "꽃")]
    [InlineData("ㅇㅏㅃㅏ", "아빠")]
    [InlineData("ㄷㅏㄹㄱㄱㅗㄱㅣ", "닭고기")]
    [InlineData("ㅇㅓㅄㅇㅓ", "없어")]
    public void Keys_gather_into_syllables_as_the_two_set_keyboard_gathers_them(string keys, string expected)
    {
        Assert.Equal(expected, Hangul.Compose(Hangul.Decompose(keys)));
    }

    [Theory]
    [InlineData("ㄱ", "ㄱ")]
    [InlineData("ㅏ", "ㅏ")]
    [InlineData("ㄱㄱ", "ㄱㄱ")]
    [InlineData("ㅏㅏ", "ㅏㅏ")]
    [InlineData("ㅋㅋㅋ", "ㅋㅋㅋ")]
    [InlineData("ㅗㅏ", "ㅘ")]
    [InlineData("ㅎㅏㅏ", "하ㅏ")]
    public void Letters_that_make_no_syllable_stay_letters(string keys, string expected)
    {
        Assert.Equal(expected, Hangul.Compose(keys));
    }

    [Fact]
    public void Everything_that_is_not_a_letter_ends_the_syllable_and_stays()
    {
        Assert.Equal("안녕, world 1!", Hangul.Compose("ㅇㅏㄴㄴㅕㅇ, world 1!"));
        Assert.Equal("hello", Hangul.Compose("hello"));
        Assert.Equal("", Hangul.Compose(""));
    }

    [Theory]
    [InlineData("dkssud", "안녕")]
    [InlineData("gksrmf", "한글")]
    [InlineData("Rhc", "꽃")]
    [InlineData("dkQk", "아빠")]
    [InlineData("Dkssud", "안녕")]
    [InlineData("rkqtdl dlTdjdy", "값이 있어요")]
    [InlineData("dkssud, 2 qjs!", "안녕, 2 번!")]
    public void English_keys_read_on_the_korean_layout(string typed, string expected)
    {
        Assert.Equal(expected, LayoutTranscoder.Transcode(typed, BundledKeyboardMaps.En, BundledKeyboardMaps.Ko));
    }

    [Theory]
    [InlineData("ㅗ디ㅣㅐ", "hello")]
    [InlineData("안녕", "dkssud")]
    [InlineData("꽃", "Rhc")]
    [InlineData("쟈ㅜ앶ㄴ", "windows")]
    [InlineData("ㅅㄷㄴㅅ 1, 2", "test 1, 2")]
    public void Korean_text_read_as_the_english_keys_that_typed_it(string typed, string expected)
    {
        Assert.Equal(expected, LayoutTranscoder.Transcode(typed, BundledKeyboardMaps.Ko, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Every_syllable_comes_back_from_its_keys()
    {
        for (var ch = '가'; ch <= '힣'; ch++)
        {
            var syllable = ch.ToString();
            var keys = Hangul.Decompose(syllable);

            Assert.All(keys, key => Assert.True(Hangul.IsJamo(key)));
            Assert.Equal(syllable, Hangul.Compose(keys));
        }
    }

    [Theory]
    [InlineData("안녕하세요")]
    [InlineData("대한민국 만세")]
    [InlineData("없었습니다")]
    [InlineData("괜찮아요, 고마워요!")]
    [InlineData("닭이 읽고 앉았다")]
    [InlineData("학교에 갔어요")]
    public void A_sentence_comes_back_from_its_keys(string text)
    {
        Assert.Equal(text, Hangul.Compose(Hangul.Decompose(text)));
    }
}
