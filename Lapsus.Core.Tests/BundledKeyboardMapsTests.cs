using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests;

public class BundledKeyboardMapsTests
{
    private const int LetterKeyCount = 26;
    private const int OemSlotStart = 36;
    private const int IsoKeySlot = 47;

    [Fact]
    public void Transcodes_leading_comma_to_ukrainian_b()
    {
        Assert.Equal("брама", LayoutTranscoder.Transcode(",hfvf", BundledKeyboardMaps.En, BundledKeyboardMaps.Uk));
    }

    [Fact]
    public void Ukrainian_pc_map_puts_и_on_b_and_і_on_s()
    {
        Assert.Equal("и", LayoutTranscoder.Transcode("b", BundledKeyboardMaps.En, BundledKeyboardMaps.Uk));
        Assert.Equal("і", LayoutTranscoder.Transcode("s", BundledKeyboardMaps.En, BundledKeyboardMaps.Uk));
        Assert.Equal("b", LayoutTranscoder.Transcode("и", BundledKeyboardMaps.Uk, BundledKeyboardMaps.En));
        Assert.Equal("s", LayoutTranscoder.Transcode("і", BundledKeyboardMaps.Uk, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Ukrainian_apple_map_swaps_і_and_и_keys()
    {
        Assert.Equal("і", LayoutTranscoder.Transcode("b", BundledKeyboardMaps.En, BundledKeyboardMaps.UkApple));
        Assert.Equal("и", LayoutTranscoder.Transcode("s", BundledKeyboardMaps.En, BundledKeyboardMaps.UkApple));
        Assert.Equal("b", LayoutTranscoder.Transcode("і", BundledKeyboardMaps.UkApple, BundledKeyboardMaps.En));
        Assert.Equal("s", LayoutTranscoder.Transcode("и", BundledKeyboardMaps.UkApple, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Ukrainian_pc_transcodes_pryvit_from_latin()
    {
        Assert.Equal("привіт", LayoutTranscoder.Transcode("ghbdsn", BundledKeyboardMaps.En, BundledKeyboardMaps.Uk));
        Assert.Equal("ghbdsn", LayoutTranscoder.Transcode("привіт", BundledKeyboardMaps.Uk, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Ukrainian_apple_transcodes_pryvit_with_swapped_vowels()
    {
        Assert.Equal("привіт",
            LayoutTranscoder.Transcode("ghsdbn", BundledKeyboardMaps.En, BundledKeyboardMaps.UkApple));
        Assert.Equal("ghsdbn",
            LayoutTranscoder.Transcode("привіт", BundledKeyboardMaps.UkApple, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Ukrainian_pc_map_includes_yi_and_ghe_with_upturn()
    {
        Assert.Equal("ї", LayoutTranscoder.Transcode("]", BundledKeyboardMaps.En, BundledKeyboardMaps.Uk));

        Assert.Equal('ґ', BundledKeyboardMaps.CreateUkSlots()[IsoKeySlot]);
        Assert.Equal("\\", LayoutTranscoder.Transcode("\\", BundledKeyboardMaps.En, BundledKeyboardMaps.Uk));
        Assert.Equal("ґ", LayoutTranscoder.Transcode("ґ", BundledKeyboardMaps.Uk, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Belarusian_jcuken_puts_short_u_and_decimal_i()
    {
        Assert.Equal("ў", LayoutTranscoder.Transcode("o", BundledKeyboardMaps.En, BundledKeyboardMaps.Be));
        Assert.Equal("і", LayoutTranscoder.Transcode("b", BundledKeyboardMaps.En, BundledKeyboardMaps.Be));
        Assert.Equal("ы", LayoutTranscoder.Transcode("s", BundledKeyboardMaps.En, BundledKeyboardMaps.Be));
        Assert.Equal("'", LayoutTranscoder.Transcode("]", BundledKeyboardMaps.En, BundledKeyboardMaps.Be));
    }

    [Fact]
    public void Bulgarian_phonetic_follows_qwerty_letters()
    {
        Assert.Equal("българия",
            LayoutTranscoder.Transcode("bylgari[", BundledKeyboardMaps.En, BundledKeyboardMaps.Bg));
        Assert.Equal("bylgari[",
            LayoutTranscoder.Transcode("българия", BundledKeyboardMaps.Bg, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Bulgarian_phonetic_2006_moves_the_letters_the_traditional_one_puts_elsewhere()
    {
        Assert.Equal("я", LayoutTranscoder.Transcode("q", BundledKeyboardMaps.En, BundledKeyboardMaps.BgPhonetic));
        Assert.Equal("в", LayoutTranscoder.Transcode("w", BundledKeyboardMaps.En, BundledKeyboardMaps.BgPhonetic));
        Assert.Equal("ь", LayoutTranscoder.Transcode("x", BundledKeyboardMaps.En, BundledKeyboardMaps.BgPhonetic));
        Assert.Equal("ч", LayoutTranscoder.Transcode("`", BundledKeyboardMaps.En, BundledKeyboardMaps.BgPhonetic));
        Assert.Equal("българия",
            LayoutTranscoder.Transcode("bylgariq", BundledKeyboardMaps.En, BundledKeyboardMaps.BgPhonetic));
        Assert.Equal("bylgariq",
            LayoutTranscoder.Transcode("българия", BundledKeyboardMaps.BgPhonetic, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Bulgarian_bds_puts_letters_on_the_comma_and_full_stop_keys()
    {
        Assert.Equal(",", LayoutTranscoder.Transcode("q", BundledKeyboardMaps.En, BundledKeyboardMaps.BgBds));
        Assert.Equal("р", LayoutTranscoder.Transcode(",", BundledKeyboardMaps.En, BundledKeyboardMaps.BgBds));
        Assert.Equal("л", LayoutTranscoder.Transcode(".", BundledKeyboardMaps.En, BundledKeyboardMaps.BgBds));
        Assert.Equal(".", LayoutTranscoder.Transcode("=", BundledKeyboardMaps.En, BundledKeyboardMaps.BgBds));
        Assert.Equal("м", LayoutTranscoder.Transcode(";", BundledKeyboardMaps.En, BundledKeyboardMaps.BgBds));
        Assert.Equal("б", LayoutTranscoder.Transcode("/", BundledKeyboardMaps.En, BundledKeyboardMaps.BgBds));
        Assert.Equal("(", LayoutTranscoder.Transcode("`", BundledKeyboardMaps.En, BundledKeyboardMaps.BgBds));
        Assert.Equal("здравей", LayoutTranscoder.Transcode("po,dlex", BundledKeyboardMaps.En, BundledKeyboardMaps.BgBds));
        Assert.Equal("po,dlex", LayoutTranscoder.Transcode("здравей", BundledKeyboardMaps.BgBds, BundledKeyboardMaps.En));
        Assert.Equal("/.dhfod,s", LayoutTranscoder.Transcode("благодаря", BundledKeyboardMaps.BgBds, BundledKeyboardMaps.En));
    }

    [Fact]
    public void The_three_bulgarian_maps_are_told_apart_by_their_letter_keys()
    {
        var traditional = BundledKeyboardMaps.CreateBgSlots();
        var phonetic = BundledKeyboardMaps.CreateBgPhoneticSlots();
        var bds = BundledKeyboardMaps.CreateBgBdsSlots();

        Assert.True(Disagreements(bds, traditional) >= 20);
        Assert.True(Disagreements(bds, phonetic) >= 20);
        Assert.True(Disagreements(phonetic, traditional) >= 4);
    }

    private static int Disagreements(char[] a, char[] b)
    {
        var n = 0;
        for (var i = 0; i < LetterKeyCount; i++)
            if (a[i] != b[i])
                n++;

        return n;
    }

    [Fact]
    public void Macedonian_maps_unique_letters()
    {
        Assert.Equal("љ", LayoutTranscoder.Transcode("q", BundledKeyboardMaps.En, BundledKeyboardMaps.Mk));
        Assert.Equal("њ", LayoutTranscoder.Transcode("w", BundledKeyboardMaps.En, BundledKeyboardMaps.Mk));
        Assert.Equal("џ", LayoutTranscoder.Transcode("x", BundledKeyboardMaps.En, BundledKeyboardMaps.Mk));
        Assert.Equal("ѕ", LayoutTranscoder.Transcode("y", BundledKeyboardMaps.En, BundledKeyboardMaps.Mk));
    }

    [Fact]
    public void Cyrillic_slot_fingerprint_matches_closest_bundled_map()
    {
        Assert.Equal("be", LayoutLanguage.FromCyrillicSlots(BundledKeyboardMaps.CreateBeSlots()));
        Assert.Equal("mk", LayoutLanguage.FromCyrillicSlots(BundledKeyboardMaps.CreateMkSlots()));
        Assert.Equal("uk", LayoutLanguage.FromCyrillicSlots(BundledKeyboardMaps.CreateUkSlots()));
        Assert.Equal("uk", LayoutLanguage.FromCyrillicSlots(BundledKeyboardMaps.CreateUkAppleSlots()));
        Assert.Equal("bg", LayoutLanguage.FromCyrillicSlots(BundledKeyboardMaps.CreateBgSlots()));
        Assert.Equal("bg", LayoutLanguage.FromCyrillicSlots(BundledKeyboardMaps.CreateBgPhoneticSlots()));
        Assert.Equal("bg", LayoutLanguage.FromCyrillicSlots(BundledKeyboardMaps.CreateBgBdsSlots()));
        Assert.Equal("ru", LayoutLanguage.FromCyrillicSlots(BundledKeyboardMaps.CreateRuSlots()));
    }

    [Fact]
    public void Cyrillic_slot_fingerprint_returns_null_when_ambiguous()
    {
        Assert.Null(LayoutLanguage.FromCyrillicSlots(new char[48]));
    }

    [Fact]
    public void The_iso_key_carries_what_windows_says_it_does()
    {
        Assert.Equal('\\', BundledKeyboardMaps.CreateHeSlots()[IsoKeySlot]);
        Assert.Equal('ѝ', BundledKeyboardMaps.CreateBgSlots()[IsoKeySlot]);
        Assert.Equal('ю', BundledKeyboardMaps.CreateBgPhoneticSlots()[IsoKeySlot]);
        Assert.Equal('ѝ', BundledKeyboardMaps.CreateBgBdsSlots()[IsoKeySlot]);
        Assert.Equal('ґ', BundledKeyboardMaps.CreateUkSlots()[IsoKeySlot]);
    }

    [Fact]
    public void Oem_slots_follow_the_order_both_backends_enumerate()
    {
        const string oemOrder = ";=,-./`[\\]'";
        var slots = BundledKeyboardMaps.CreateEnSlots();

        Assert.Equal(oemOrder, new string(slots, OemSlotStart, oemOrder.Length));
        Assert.Equal('\0', slots[IsoKeySlot]);
    }

    [Fact]
    public void Hebrew_map_round_trips_letters_on_punctuation_keys()
    {
        Assert.Equal("שלום", LayoutTranscoder.Transcode("akuo", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal("akuo", LayoutTranscoder.Transcode("שלום", BundledKeyboardMaps.He, BundledKeyboardMaps.En));

        Assert.Equal("ף", LayoutTranscoder.Transcode(";", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal("ת", LayoutTranscoder.Transcode(",", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal("ץ", LayoutTranscoder.Transcode(".", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal(",", LayoutTranscoder.Transcode("'", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal("ך", LayoutTranscoder.Transcode("l", BundledKeyboardMaps.En, BundledKeyboardMaps.He));

        Assert.Equal("תודה", LayoutTranscoder.Transcode(",usv", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
    }

    [Fact]
    public void Hebrew_map_keeps_punctuation_that_moved_onto_letter_keys()
    {
        Assert.Equal("/", LayoutTranscoder.Transcode("q", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal("'", LayoutTranscoder.Transcode("w", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal(".", LayoutTranscoder.Transcode("/", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal(";", LayoutTranscoder.Transcode("`", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
    }

    [Fact]
    public void Gershayim_sits_on_the_shifted_apostrophe_key_on_both_maps()
    {
        Assert.Equal("צה\"ל", LayoutTranscoder.Transcode("mv\"k", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal("mv\"k", LayoutTranscoder.Transcode("צה\"ל", BundledKeyboardMaps.He, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Us_shifted_symbols_have_a_key_but_do_not_become_letters_elsewhere()
    {
        Assert.Equal("?", LayoutTranscoder.Transcode("?", BundledKeyboardMaps.En, BundledKeyboardMaps.Ru));
        Assert.Equal("!", LayoutTranscoder.Transcode("!", BundledKeyboardMaps.En, BundledKeyboardMaps.Uk));
        Assert.Equal("Привет!", LayoutTranscoder.Transcode("Ghbdtn!", BundledKeyboardMaps.En, BundledKeyboardMaps.Ru));
    }

    [Fact]
    public void Hebrew_map_mirrors_the_brackets_as_the_os_layout_does()
    {
        Assert.Equal("]", LayoutTranscoder.Transcode("[", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal("[", LayoutTranscoder.Transcode("]", BundledKeyboardMaps.En, BundledKeyboardMaps.He));
        Assert.Equal("[", LayoutTranscoder.Transcode("]", BundledKeyboardMaps.He, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Greek_qwerty_map_round_trips_common_letters()
    {
        Assert.Equal("α", LayoutTranscoder.Transcode("a", BundledKeyboardMaps.En, BundledKeyboardMaps.El));
        Assert.Equal("ω", LayoutTranscoder.Transcode("v", BundledKeyboardMaps.En, BundledKeyboardMaps.El));
        Assert.Equal("ς", LayoutTranscoder.Transcode("w", BundledKeyboardMaps.En, BundledKeyboardMaps.El));
        Assert.Equal("σ", LayoutTranscoder.Transcode("s", BundledKeyboardMaps.En, BundledKeyboardMaps.El));
        Assert.Equal("καλημερα",
            LayoutTranscoder.Transcode("kalhmera", BundledKeyboardMaps.En, BundledKeyboardMaps.El));
        Assert.Equal("kalhmera",
            LayoutTranscoder.Transcode("καλημερα", BundledKeyboardMaps.El, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Georgian_qwerty_map_is_the_language_transliteration()
    {
        Assert.Equal("გამარჯობა",
            LayoutTranscoder.Transcode("gamarjoba", BundledKeyboardMaps.En, BundledKeyboardMaps.Ka));
        Assert.Equal("gamarjoba",
            LayoutTranscoder.Transcode("გამარჯობა", BundledKeyboardMaps.Ka, BundledKeyboardMaps.En));
    }

    [Theory]
    [InlineData("T", "თ")]
    [InlineData("S", "შ")]
    [InlineData("R", "ღ")]
    [InlineData("C", "ჩ")]
    [InlineData("J", "ჟ")]
    [InlineData("Z", "ძ")]
    [InlineData("W", "ჭ")]
    public void Georgian_shifted_level_carries_its_seven_extra_letters(string latin, string georgian)
    {
        Assert.Equal(georgian, LayoutTranscoder.Transcode(latin, BundledKeyboardMaps.En, BundledKeyboardMaps.Ka));
        Assert.Equal(latin, LayoutTranscoder.Transcode(georgian, BundledKeyboardMaps.Ka, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Georgian_shifted_letter_inside_a_word_round_trips()
    {
        Assert.Equal("თბილისი", LayoutTranscoder.Transcode("Tbilisi", BundledKeyboardMaps.En, BundledKeyboardMaps.Ka));
        Assert.Equal("Tbilisi", LayoutTranscoder.Transcode("თბილისი", BundledKeyboardMaps.Ka, BundledKeyboardMaps.En));
    }

    [Fact]
    public void Georgian_map_covers_all_33_modern_letters()
    {
        const string alphabet = "აბგდევზთიკლმნოპჟრსტუფქღყშჩცძწჭხჯჰ";

        var latin = LayoutTranscoder.Transcode(alphabet, BundledKeyboardMaps.Ka, BundledKeyboardMaps.En);
        Assert.All(latin, c => Assert.True(char.IsAsciiLetter(c), $"'{c}' has no key on the Georgian map"));
        Assert.Equal(alphabet, LayoutTranscoder.Transcode(latin, BundledKeyboardMaps.En, BundledKeyboardMaps.Ka));
    }

    [Fact]
    public void Georgian_map_puts_letters_on_the_letter_keys_only()
    {
        var slots = BundledKeyboardMaps.CreateKaSlots();
        var shifted = BundledKeyboardMaps.CreateKaShiftedSlots();

        for (var slot = LetterKeyCount; slot < slots.Length; slot++)
        {
            Assert.Null(Scripts.Of(slots[slot]));
            Assert.Null(Scripts.Of(shifted[slot]));
        }
    }
}
