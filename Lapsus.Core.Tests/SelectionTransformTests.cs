using Lapsus.Core.Layout;
using Lapsus.Core.Text;

namespace Lapsus.Core.Tests;

public class CaseCycleTests
{
    [Theory]
    [InlineData("привет", "Привет")]
    [InlineData("Привет", "ПРИВЕТ")]
    [InlineData("ПРИВЕТ", "привет")]
    [InlineData("hello world", "Hello world")]
    [InlineData("Hello world", "HELLO WORLD")]
    [InlineData("HELLO WORLD", "hello world")]
    public void Walks_the_cycle(string input, string expected)
    {
        Assert.Equal(expected, CaseCycle.Next(input));
    }

    [Fact]
    public void Three_presses_return_to_the_start()
    {
        var once = CaseCycle.Next("привет мир");
        var twice = CaseCycle.Next(once);
        Assert.Equal("привет мир", CaseCycle.Next(twice));
    }

    [Fact]
    public void Sentence_case_starts_after_a_full_stop()
    {
        Assert.Equal("Раз. Два! Три? Четыре", CaseCycle.Next("раз. два! три? четыре"));
    }

    [Fact]
    public void Sentence_case_starts_after_a_line_break()
    {
        Assert.Equal("Раз\nДва", CaseCycle.Next("раз\nдва"));
    }

    [Fact]
    public void Punctuation_inside_a_sentence_does_not_capitalise()
    {
        Assert.Equal("«Да», — сказал он", CaseCycle.Next("«да», — сказал он"));
    }

    [Theory]
    [InlineData("שלום")]
    [InlineData("مرحبا")]
    [InlineData("12345")]
    [InlineData("")]
    public void Text_with_no_case_is_left_alone_by_the_case_cycle(string input)
    {
        Assert.Equal(input, CaseCycle.Next(input));
    }

    [Theory]
    [InlineData("καλός", "Καλός")]
    [InlineData("Καλός", "ΚΑΛΌΣ")]
    [InlineData("ΚΑΛΌΣ", "καλός")]
    [InlineData("ΟΔΥΣΣΕΥΣ", "οδυσσευς")]
    [InlineData("ΣΣ ΣΑΣ", "σς σας")]
    public void Greek_keeps_its_final_sigma_through_the_cycle(string input, string expected)
    {
        Assert.Equal(expected, CaseCycle.Next(input));
    }

    [Fact]
    public void Three_presses_return_to_the_start_in_greek()
    {
        var once = CaseCycle.Next("καλημέρα κόσμος");
        var twice = CaseCycle.Next(once);
        Assert.Equal("καλημέρα κόσμος", CaseCycle.Next(twice));
    }

    [Fact]
    public void Georgian_cycles_between_mkhedruli_and_mtavruli()
    {
        var upper = CaseCycle.Next("გამარჯობა");
        Assert.Equal("ᲒᲐᲛᲐᲠᲯᲝᲑᲐ", upper);
        Assert.Equal("გამარჯობა", CaseCycle.Next(upper));
    }

    [Fact]
    public void Georgian_never_gets_a_sentence_initial_capital()
    {
        var text = "გამარჯობა. როგორ ხარ?";
        for (var press = 0; press < 4; press++)
        {
            text = CaseCycle.Next(text);
            var mtavruli = text.Count(c => c is >= 'Ა' and <= 'Ჺ');
            Assert.True(
                mtavruli == 0 || mtavruli == text.Count(char.IsLetter),
                $"press {press + 1} produced mixed cases: {text}");
        }
    }

    [Fact]
    public void Latin_beside_georgian_keeps_its_sentence_case()
    {
        Assert.Equal("Hello გამარჯობა", CaseCycle.Next("hello გამარჯობა"));
    }
}

public class TransliteratorTests
{
    [Theory]
    [InlineData("привет", "privet")]
    [InlineData("щи", "shchi")]
    [InlineData("жук", "zhuk")]
    [InlineData("цена", "tsena")]
    [InlineData("хорошо", "khorosho")]
    [InlineData("йод", "jod")]
    public void Cyrillic_becomes_latin(string input, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(input));
    }

    [Theory]
    [InlineData("привет")]
    [InlineData("щи")]
    [InlineData("жук")]
    [InlineData("цена")]
    [InlineData("хорошо")]
    [InlineData("йод")]
    [InlineData("съел")]
    [InlineData("Москва")]
    public void Pressing_it_twice_gets_the_word_back(string word)
    {
        var expected = word.Replace("ъ", string.Empty);
        Assert.Equal(expected, Transliterator.Convert(Transliterator.Convert(word)));
    }

    [Fact]
    public void Latin_becomes_cyrillic()
    {
        Assert.Equal("привет", Transliterator.Convert("privet"));
    }

    [Fact]
    public void Digraphs_win_over_single_letters()
    {
        Assert.Equal("щи", Transliterator.Convert("shchi"));
        Assert.Equal("ша", Transliterator.Convert("sha"));
        Assert.Equal("са", Transliterator.Convert("sa"));
    }

    [Theory]
    [InlineData("Жук", "Zhuk")]
    [InlineData("ЖУК", "ZHUK")]
    [InlineData("Москва", "Moskva")]
    [InlineData("МОСКВА", "MOSKVA")]
    public void Capitals_survive_a_multi_letter_mapping(string input, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(input));
    }

    [Fact]
    public void Capitals_survive_the_way_back()
    {
        Assert.Equal("Жук", Transliterator.Convert("Zhuk"));
        Assert.Equal("ЖУК", Transliterator.Convert("ZHUK"));
    }

    [Fact]
    public void Ukrainian_and_belarusian_letters_are_covered()
    {
        Assert.Equal("kyjiw", Transliterator.Convert("кыїў"));
    }

    [Theory]
    [InlineData("Київ", "Kyiv")]
    [InlineData("Україна", "Ukraina")]
    [InlineData("Ужгород", "Uzhhorod")]
    [InlineData("її", "yii")]
    [InlineData("Юрій", "Yurii")]
    [InlineData("щастя", "shchastia")]
    [InlineData("ґанок", "ganok")]
    [InlineData("об'єкт", "obiekt")]
    [InlineData("Жовтень", "Zhovten")]
    public void Ukrainian_follows_its_own_romanisation(string input, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(input, Script.Cyrillic, "uk"));
    }

    [Theory]
    [InlineData("България", "Balgariya")]
    [InlineData("Търново", "Tarnovo")]
    [InlineData("щъркел", "shtarkel")]
    [InlineData("София", "Sofiya")]
    [InlineData("здравей", "zdravey")]
    public void Bulgarian_follows_its_own_romanisation(string input, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(input, Script.Cyrillic, "bg"));
    }

    [Theory]
    [InlineData("Беларусь", "Belarus")]
    [InlineData("Мінск", "Minsk")]
    [InlineData("Свабода", "Svaboda")]
    [InlineData("ўсход", "wskhod")]
    public void Belarusian_follows_its_own_romanisation(string input, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(input, Script.Cyrillic, "be"));
    }

    [Fact]
    public void An_unknown_language_stays_on_the_russian_table()
    {
        Assert.Equal("Kijiv", Transliterator.Convert("Київ"));
        Assert.Equal("Moskva", Transliterator.Convert("Москва", Script.Cyrillic, "ru"));
        Assert.Equal("gorod", Transliterator.Convert("город", Script.Cyrillic, "ru"));
        Assert.Equal("horod", Transliterator.Convert("город", Script.Cyrillic, "uk"));
    }

    [Fact]
    public void Macedonian_writes_its_own_digraph_letters()
    {
        Assert.Equal("Skopje", Transliterator.Convert("Скопје", Script.Cyrillic, "mk"));
        Assert.Equal("njiv", Transliterator.Convert("њив", Script.Cyrillic, "mk"));
    }

    [Theory]
    [InlineData("გამარჯობა")]
    [InlineData("2024")]
    public void Anything_else_is_left_alone(string input)
    {
        Assert.Equal(input, Transliterator.Convert(input));
    }

    [Fact]
    public void Digits_and_punctuation_pass_through()
    {
        Assert.Equal("dom-2!", Transliterator.Convert("дом-2!"));
    }

    [Theory]
    [InlineData("καλημέρα", "kalimera")]
    [InlineData("Αθήνα", "Athina")]
    [InlineData("ευχαριστώ", "eycharisto")]
    [InlineData("ψάρι", "psari")]
    [InlineData("ξένος", "xenos")]
    [InlineData("φως", "fos")]
    public void Greek_becomes_latin(string input, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(input));
    }

    [Fact]
    public void Omicron_upsilon_is_one_letter_on_the_way_out()
    {
        Assert.Equal("mou", Transliterator.Convert("μου"));
        Assert.Equal("pou", Transliterator.Convert("πού"));
        Assert.Equal("μου", Transliterator.Convert("mou", Script.Greek));
    }

    [Theory]
    [InlineData("μας")]
    [InlineData("και")]
    [InlineData("φιλος")]
    [InlineData("τραπεζι")]
    [InlineData("παιδι")]
    [InlineData("αυριο")]
    public void Greek_without_eta_omega_or_a_tonos_survives_the_round_trip(string word)
    {
        Assert.Equal(word, Transliterator.Convert(Transliterator.Convert(word), Script.Greek));
    }

    [Fact]
    public void Eta_omega_and_the_tonos_do_not_come_back()
    {
        Assert.Equal("καλιμερα", Transliterator.Convert(Transliterator.Convert("καλημέρα"), Script.Greek));
    }

    [Theory]
    [InlineData("filos", "φιλος")]
    [InlineData("isis", "ισις")]
    [InlineData("sos s", "σος ς")]
    public void The_last_s_of_a_word_becomes_a_final_sigma(string input, string expected)
    {
        Assert.Equal(expected, Transliterator.Convert(input, Script.Greek));
    }

    [Fact]
    public void Capitals_survive_both_ways_and_never_take_the_final_form()
    {
        Assert.Equal("ΣΟΣ", Transliterator.Convert("SOS", Script.Greek));
        Assert.Equal("ATHINA", Transliterator.Convert("ΑΘΗΝΑ"));
        Assert.Equal("Αθινα", Transliterator.Convert("Athina", Script.Greek));
    }

    [Fact]
    public void Latin_goes_where_the_caller_says()
    {
        Assert.Equal("ниа", Transliterator.Convert("nia"));
        Assert.Equal("νια", Transliterator.Convert("nia", Script.Greek));
    }

    [Fact]
    public void Greeklish_h_and_w_reach_the_letters_the_way_out_cannot()
    {
        Assert.Equal("καλημερα", Transliterator.Convert("kalhmera", Script.Greek));
        Assert.Equal("φως", Transliterator.Convert("fws", Script.Greek));
    }
}

public class VisualRtlTests
{
    private const string Shalom = "שלום";
    private const string ShalomReversed = "םולש";

    [Fact]
    public void A_reversed_word_is_turned_round()
    {
        Assert.Equal(Shalom, VisualRtl.Flip(ShalomReversed));
    }

    [Fact]
    public void Flipping_twice_is_the_identity()
    {
        Assert.Equal(ShalomReversed, VisualRtl.Flip(VisualRtl.Flip(ShalomReversed)));
    }

    [Fact]
    public void Numbers_keep_their_own_order()
    {
        Assert.Equal($"{Shalom} 2024", VisualRtl.Flip($"2024 {ShalomReversed}"));
    }

    [Fact]
    public void Latin_words_keep_their_own_order()
    {
        Assert.Equal($"{Shalom} Lapsus", VisualRtl.Flip($"Lapsus {ShalomReversed}"));
    }

    [Fact]
    public void Paired_punctuation_is_mirrored()
    {
        Assert.Equal($"({Shalom})", VisualRtl.Flip($"({ShalomReversed})"));
    }

    [Fact]
    public void Lines_keep_their_order_and_their_breaks()
    {
        Assert.Equal($"{Shalom}\n{Shalom}", VisualRtl.Flip($"{ShalomReversed}\n{ShalomReversed}"));
    }

    [Fact]
    public void Outer_whitespace_stays_put()
    {
        Assert.Equal($"  {Shalom} ", VisualRtl.Flip($"  {ShalomReversed} "));
    }

    [Fact]
    public void Combining_marks_stay_on_their_letter()
    {
        const string withNiqqud = "שָׁלוֹם";
        var reversed = VisualRtl.Flip(withNiqqud);
        Assert.Equal(withNiqqud, VisualRtl.Flip(reversed));
        Assert.StartsWith("ם", reversed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("привет мир")]
    [InlineData("")]
    public void Text_with_nothing_right_to_left_is_left_alone(string input)
    {
        Assert.Equal(input, VisualRtl.Flip(input));
    }
}
