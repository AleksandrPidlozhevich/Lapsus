using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests;

public class NeuralPhraseRewriterTests
{
    [Fact]
    public void Sanitize_strips_quotes_and_extra_lines()
    {
        var cleaned = NeuralPhraseRewriter.SanitizeModelOutput("\"car\"\nBecause it was mistyped.", "сфк");
        Assert.Equal("car", cleaned);
    }

    [Fact]
    public void Sanitize_rejects_overlong_chatter()
    {
        var cleaned = NeuralPhraseRewriter.SanitizeModelOutput(new string('x', 200), "ab");
        Assert.Equal(string.Empty, cleaned);
    }

    [Fact]
    public void ResolveTarget_maps_script_change_to_candidate()
    {
        var active = new LayoutSource(Script.Cyrillic, "uk", BundledKeyboardMaps.Uk, "uk");
        var candidates = new LayoutCandidate[]
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en")
        };

        var (target, id) = NeuralPhraseRewriter.ResolveTarget("сфк", "car", active, candidates, null);
        Assert.Equal(KeyboardLayout.En, target);
        Assert.Equal("en", id);
    }

    [Fact]
    public void ResolveTarget_follows_the_word_that_changed_script_not_the_line()
    {
        var active = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En, "en");
        var candidates = new LayoutCandidate[]
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en"),
            new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru", "ru")
        };

        var (target, id) = NeuralPhraseRewriter.ResolveTarget(
            "привет мир ntrcn", "привет мир текст", active, candidates, null);

        Assert.Equal(KeyboardLayout.Ru, target);
        Assert.Equal("ru", id);
    }

    [Fact]
    public void ResolveTarget_keeps_null_when_words_move_two_different_ways()
    {
        var active = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En, "en");
        var candidates = new LayoutCandidate[]
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en"),
            new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru", "ru")
        };

        var (target, id) = NeuralPhraseRewriter.ResolveTarget(
            "ntrcn привет", "текст hello", active, candidates, null);

        Assert.Null(target);
        Assert.Null(id);
    }

    [Fact]
    public void ResolveTarget_keeps_null_when_script_unchanged()
    {
        var active = new LayoutSource(Script.Cyrillic, "uk", BundledKeyboardMaps.Uk, "uk");
        var candidates = new LayoutCandidate[]
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en")
        };

        var (target, id) = NeuralPhraseRewriter.ResolveTarget("молоко", "молоко", active, candidates, null);
        Assert.Null(target);
        Assert.Null(id);
    }

    [Fact]
    public void CorrectPhrase_uses_llm_and_marks_changed()
    {
        var llm = new FakeLlm("car");
        var rewriter = new NeuralPhraseRewriter(llm);
        var active = new LayoutSource(Script.Cyrillic, "uk", BundledKeyboardMaps.Uk, "uk");
        var installed = new[] { active };
        var candidates = new LayoutCandidate[]
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en")
        };

        var result = rewriter.CorrectPhrase("сфк", active, installed, candidates);
        Assert.True(result.Changed);
        Assert.Equal("car", result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Fact]
    public void A_word_on_the_leave_alone_list_comes_back_as_typed_from_the_model()
    {
        var result = RewriteWithList("ghbdtn vfrc", modelSays: "привет макс", listed: "vfrc");

        Assert.True(result.Changed);
        Assert.Equal("привет vfrc", result.Corrected);
        Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
    }

    [Fact]
    public void An_answer_that_loses_track_of_a_listed_word_is_not_applied()
    {
        var result = RewriteWithList("ghbdtn vfrc", modelSays: "приветмакс", listed: "vfrc");

        Assert.False(result.Changed);
        Assert.Equal("ghbdtn vfrc", result.Corrected);
    }

    [Fact]
    public void An_answer_that_only_moved_a_listed_word_changes_nothing()
    {
        var result = RewriteWithList("vfrc", modelSays: "макс", listed: "vfrc");

        Assert.False(result.Changed);
    }

    private static PhraseCorrection RewriteWithList(string typed, string modelSays, string listed)
    {
        var active = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En, "en");
        var candidates = new LayoutCandidate[]
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en"),
            new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru", "ru")
        };

        return new NeuralPhraseRewriter(new FakeLlm(modelSays), exceptions: new WordExceptions([listed]))
            .CorrectPhrase(typed, active, [active], candidates);
    }

    [Fact]
    public void CorrectPhrase_falls_back_to_layout_remap_when_llm_echoes()
    {
        var llm = new FakeLlm("ntrcn");
        var rewriter = new NeuralPhraseRewriter(llm);
        var active = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En, "en");
        var candidates = new LayoutCandidate[]
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk"),
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en")
        };

        var result = rewriter.CorrectPhrase("ntrcn", active, [active], candidates, KeyboardLayout.Uk);
        Assert.True(result.Changed);
        Assert.Equal("текст", result.Corrected);
        Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
    }

    [Fact]
    public void Layout_remap_fallback_keeps_the_typed_whitespace_exactly()
    {
        var llm = new FakeLlm("ntrcn ");
        var rewriter = new NeuralPhraseRewriter(llm);
        var active = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En, "en");
        var candidates = new LayoutCandidate[]
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk")
        };

        var result = rewriter.CorrectPhrase("ntrcn ", active, [active], candidates, KeyboardLayout.Uk);

        Assert.True(result.Changed);
        Assert.Equal("текст ", result.Corrected);
    }

    [Fact]
    public void The_model_is_told_how_long_an_answer_the_fragment_is_worth()
    {
        var llm = new FakeLlm("текст");
        var rewriter = new NeuralPhraseRewriter(llm);
        var active = new LayoutSource(Script.Latin, "en", BundledKeyboardMaps.En, "en");
        var candidates = new LayoutCandidate[]
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk")
        };

        rewriter.CorrectPhrase("ntrcn", active, [active], candidates);

        Assert.Equal("ntrcn".Length, llm.LastAnswerChars);
        Assert.True(llm.LastUserPrompt.Length > 100, "the prompt is far longer than the fragment");
    }

    [Theory]
    [InlineData("ьн", "my")]
    [InlineData("םמ", "on")]
    public void An_echoed_run_impossible_in_its_own_script_falls_back_to_the_remap(
        string typed, string expected)
    {
        var script = Scripts.Dominant(typed)!.Value;
        var map = script == Script.Cyrillic ? BundledKeyboardMaps.Uk : BundledKeyboardMaps.He;
        var active = new LayoutSource(script, "src", map, "src");
        var candidates = new LayoutCandidate[]
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en")
        };

        var result = new NeuralPhraseRewriter(new FakeLlm(typed))
            .CorrectPhrase(typed, active, [active], candidates);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Corrected);
        Assert.Equal(KeyboardLayout.En, result.TargetLayout);
    }

    [Theory]
    [InlineData("привет")]
    [InlineData("שלום")]
    public void An_echoed_word_its_script_can_write_is_left_exactly_as_typed(string typed)
    {
        var script = Scripts.Dominant(typed)!.Value;
        var map = script == Script.Cyrillic ? BundledKeyboardMaps.Uk : BundledKeyboardMaps.He;
        var active = new LayoutSource(script, "src", map, "src");
        var candidates = new LayoutCandidate[]
        {
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en")
        };

        var result = new NeuralPhraseRewriter(new FakeLlm(typed))
            .CorrectPhrase(typed, active, [active], candidates);

        Assert.False(result.Changed);
        Assert.Equal(typed, result.Corrected);
    }

    [Fact]
    public void Remaps_carry_no_outer_whitespace()
    {
        var candidates = new LayoutCandidate[]
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk")
        };

        var remaps = NeuralRewritePrompt.CollectRemaps(
            "  ntrcn  ", BundledKeyboardMaps.En, candidates, KeyboardLayout.Uk);

        Assert.Equal(["текст"], remaps);
    }

    [Theory]
    [InlineData(Script.Greek, "γεια")]
    [InlineData(Script.Hebrew, "שלום")]
    [InlineData(Script.Arabic, "مرحبا")]
    [InlineData(Script.Georgian, "გამარჯობა")]
    public void The_installed_layout_picks_the_examples_with_no_preference_set(Script script, string expected)
    {
        var user = NeuralRewritePrompt.BuildUser(
            "test", BundledKeyboardMaps.En, [Candidate(script)], preferred: null);

        Assert.Contains(expected, user, StringComparison.Ordinal);
        Assert.DoesNotContain("привет", user, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stated_preference_outranks_the_installed_layouts()
    {
        var user = NeuralRewritePrompt.BuildUser(
            "test", BundledKeyboardMaps.En,
            [Candidate(Script.Greek), Candidate(Script.Cyrillic)],
            KeyboardLayout.El);

        Assert.Contains("γεια", user, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_non_latin_layouts_are_all_taught_to_the_model()
    {
        var user = NeuralRewritePrompt.BuildUser(
            "test", BundledKeyboardMaps.En,
            [Candidate(Script.Greek), Candidate(Script.Cyrillic)],
            preferred: null);

        Assert.Contains("текст", user, StringComparison.Ordinal);
        Assert.Contains("γεια", user, StringComparison.Ordinal);
        Assert.True(
            user.IndexOf("текст", StringComparison.Ordinal) < user.IndexOf("γεια", StringComparison.Ordinal),
            "the commonest case comes first");
    }

    [Fact]
    public void The_no_op_example_is_listed_once_however_many_scripts_there_are()
    {
        var user = NeuralRewritePrompt.BuildUser(
            "test", BundledKeyboardMaps.En,
            [Candidate(Script.Greek), Candidate(Script.Cyrillic), Candidate(Script.Georgian)],
            preferred: null);

        Assert.Equal(1, user.Split("hello => hello").Length - 1);
    }

    [Fact]
    public void A_fourth_layout_does_not_keep_growing_the_prompt()
    {
        var user = NeuralRewritePrompt.BuildUser(
            "test", BundledKeyboardMaps.En,
            [
                Candidate(Script.Cyrillic), Candidate(Script.Greek),
                Candidate(Script.Hebrew), Candidate(Script.Arabic)
            ],
            preferred: null);

        Assert.DoesNotContain("مرحبا", user, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Script.Cyrillic)]
    [InlineData(Script.Greek)]
    [InlineData(Script.Hebrew)]
    [InlineData(Script.Arabic)]
    [InlineData(Script.Georgian)]
    public void Every_example_is_a_real_transcode(Script script)
    {
        var user = NeuralRewritePrompt.BuildUser(
            "test", BundledKeyboardMaps.En, [Candidate(script)], preferred: null);

        var target = BundledMapFor(script);
        var checkedAny = false;

        foreach (var raw in user.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("Examples", StringComparison.Ordinal))
                continue;

            var arrow = line.IndexOf(" => ", StringComparison.Ordinal);
            if (arrow < 0)
                continue;

            var keys = line[..arrow];
            var meant = line[(arrow + 4)..];
            if (keys == meant)
                continue;

            var into = LayoutTranscoder.Transcode(keys, BundledKeyboardMaps.En, target);
            var outOf = LayoutTranscoder.Transcode(keys, target, BundledKeyboardMaps.En);
            Assert.True(
                meant == into || meant == outOf,
                $"'{line}' is no real transcode: into={into}, out of={outOf}");
            checkedAny = true;
        }

        Assert.True(checkedAny, "no example pairs were found to check");
    }

    [Fact]
    public void The_examples_teach_the_way_in_and_the_way_out()
    {
        var user = NeuralRewritePrompt.BuildUser(
            "test", BundledKeyboardMaps.En, [Candidate(Script.Cyrillic)], preferred: null);

        Assert.Contains("vjkjrj => молоко", user, StringComparison.Ordinal);
        Assert.Contains("сфк => car", user, StringComparison.Ordinal);
        Assert.DoesNotContain("привет", user, StringComparison.Ordinal);
    }

    [Fact]
    public void Russian_examples_appear_only_when_Russian_is_the_preferred_layout()
    {
        var uk = NeuralRewritePrompt.BuildUser(
            "test", BundledKeyboardMaps.En, [Candidate(Script.Cyrillic)], KeyboardLayout.Uk);
        var ru = NeuralRewritePrompt.BuildUser(
            "test", BundledKeyboardMaps.En, [Candidate(Script.Cyrillic)], KeyboardLayout.Ru);

        Assert.DoesNotContain("привет", uk, StringComparison.Ordinal);
        Assert.Contains("ghbdtn => привет", ru, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_head_is_the_same_whichever_direction_the_user_is_typing()
    {
        LayoutCandidate[] candidates =
        [
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en"),
            Candidate(Script.Cyrillic)
        ];

        var latinIn = NeuralRewritePrompt.BuildUser("ghbdtn", BundledKeyboardMaps.En, candidates);
        var cyrillicIn = NeuralRewritePrompt.BuildUser("руддщ", BundledKeyboardMaps.Ru, candidates);

        var head = latinIn.IndexOf("The intended text", StringComparison.Ordinal);
        Assert.True(head > 0, "the remaps line is where the prompt is allowed to start differing");
        Assert.Equal(latinIn[..head], cyrillicIn[..head]);
    }

    private static LayoutCandidate Candidate(Script script)
    {
        return new LayoutCandidate(script, null, BundledMapFor(script));
    }

    private static KeyboardMap BundledMapFor(Script script)
    {
        return script switch
        {
            Script.Cyrillic => BundledKeyboardMaps.Ru,
            Script.Greek => BundledKeyboardMaps.El,
            Script.Hebrew => BundledKeyboardMaps.He,
            Script.Arabic => BundledKeyboardMaps.Ar,
            _ => BundledKeyboardMaps.Ka
        };
    }

    [Fact]
    public void Prompt_system_states_the_only_corrected_text_rule()
    {
        var system = NeuralRewritePrompt.BuildSystem();
        Assert.Contains("ONLY the corrected text", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never translate", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_system_adds_preferred_language_hint()
    {
        var system = NeuralRewritePrompt.BuildSystem(KeyboardLayout.Uk);
        Assert.Contains("Ukrainian", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_user_lists_candidate_remaps_and_the_text()
    {
        var candidates = new LayoutCandidate[]
        {
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk")
        };

        var user = NeuralRewritePrompt.BuildUser("ntrcn", BundledKeyboardMaps.En, candidates, KeyboardLayout.Uk);

        Assert.Contains("текст", user, StringComparison.Ordinal);
        Assert.Contains("Text: ntrcn", user, StringComparison.Ordinal);
        Assert.EndsWith("Corrected:", user, StringComparison.Ordinal);
    }

    [Fact]
    public void The_candidate_list_offers_leaving_the_text_alone()
    {
        var user = NeuralRewritePrompt.BuildUser(
            "guy;", BundledKeyboardMaps.En, [Candidate(Script.Cyrillic)]);

        Assert.Contains("as it stands", user, StringComparison.Ordinal);
    }

    public class WithADictionaryAdviser
    {
        private static readonly LayoutSource Active = new(Script.Latin, "en", BundledKeyboardMaps.En, "en");

        private static readonly LayoutCandidate[] Candidates =
        [
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk"),
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en")
        ];

        private static readonly LayoutCandidate Georgian =
            new(Script.Georgian, KeyboardLayout.Ka, BundledKeyboardMaps.Ka, "ka", "ka");

        [Fact]
        public void An_echo_falls_back_to_the_dictionary_rather_than_to_a_raw_remap()
        {
            var advice = new PhraseCorrection("gjk,dfyyz", "полювання", true, KeyboardLayout.Uk, "uk");
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("gjk,dfyyz"), new FakeAdviser(advice, Script.Cyrillic, Script.Latin));

            var result = rewriter.CorrectPhrase("gjk,dfyyz", Active, [Active], Candidates);

            Assert.True(result.Changed);
            Assert.Equal("полювання", result.Corrected);
            Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
        }

        [Fact]
        public void The_dictionary_reading_is_offered_to_the_model()
        {
            const string typed = "gjk,dfyyz yf rfxjr";
            var advice = new PhraseCorrection(typed, "полювання на качок", true, KeyboardLayout.Uk, "uk");
            var llm = new FakeLlm("полювання на качок");
            var rewriter = new NeuralPhraseRewriter(llm, new FakeAdviser(advice, Script.Cyrillic));

            rewriter.CorrectPhrase(typed, Active, [Active], Candidates);

            var rawRemap = NeuralRewritePrompt.CollectRemaps(typed, BundledKeyboardMaps.En, Candidates)[0];

            Assert.Contains("полювання на качок", llm.LastUserPrompt, StringComparison.Ordinal);
            Assert.True(
                llm.LastUserPrompt.IndexOf("полювання на качок", StringComparison.Ordinal) <
                llm.LastUserPrompt.IndexOf(rawRemap, StringComparison.Ordinal),
                "the dictionary's reading comes before the raw remap");
        }

        [Fact]
        public void One_word_the_dictionary_repairs_never_reaches_the_model()
        {
            var advice = new PhraseCorrection("gjk,dfyyz", "полювання", true, KeyboardLayout.Uk, "uk");
            var llm = new FakeLlm("полбвання");
            var rewriter = new NeuralPhraseRewriter(
                llm, new FakeAdviser(advice, Script.Latin, Script.Cyrillic));

            var result = rewriter.CorrectPhrase("gjk,dfyyz", Active, [Active], Candidates);

            Assert.Equal("полювання", result.Corrected);
            Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
            Assert.Equal("", llm.LastUserPrompt);
        }

        [Fact]
        public void A_reading_no_dictionary_can_judge_sends_even_one_word_to_the_model()
        {
            var greek = new LayoutCandidate(Script.Greek, KeyboardLayout.El, BundledKeyboardMaps.El, "el", "el");
            var advice = new PhraseCorrection("geia", "пуф", true, KeyboardLayout.Ru, "ru");
            var llm = new FakeLlm("γεια");
            var rewriter = new NeuralPhraseRewriter(
                llm, new FakeAdviser(advice, Script.Latin, Script.Cyrillic));

            var result = rewriter.CorrectPhrase("geia", Active, [Active], [.. Candidates, greek]);

            Assert.Equal("γεια", result.Corrected);
            Assert.NotEqual("", llm.LastUserPrompt);
        }

        [Fact]
        public void The_trailing_space_of_a_typed_line_still_counts_as_one_word()
        {
            var advice = new PhraseCorrection("ghbdtn ", "привет ", true, KeyboardLayout.Ru, "ru");
            var llm = new FakeLlm("привет");
            var rewriter = new NeuralPhraseRewriter(
                llm, new FakeAdviser(advice, Script.Latin, Script.Cyrillic));

            var result = rewriter.CorrectPhrase("ghbdtn ", Active, [Active], Candidates);

            Assert.Equal("привет ", result.Corrected);
            Assert.Equal("", llm.LastUserPrompt);
        }

        [Fact]
        public void One_word_the_dictionary_declines_still_goes_to_the_model()
        {
            var unchanged = new PhraseCorrection("gamarjoba", "gamarjoba", false, null);
            var llm = new FakeLlm("გამარჯობა");
            var rewriter = new NeuralPhraseRewriter(llm, new FakeAdviser(unchanged, Script.Cyrillic));

            var result = rewriter.CorrectPhrase("gamarjoba", Active, [Active], [.. Candidates, Georgian]);

            Assert.Equal("გამარჯობა", result.Corrected);
            Assert.NotEqual("", llm.LastUserPrompt);
        }

        [Fact]
        public void An_invented_answer_loses_to_the_dictionary()
        {
            var advice = new PhraseCorrection("ntrcn", "текст", true, KeyboardLayout.Uk, "uk");
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("нтрнк"), new FakeAdviser(advice, Script.Cyrillic));

            var result = rewriter.CorrectPhrase("ntrcn", Active, [Active], Candidates);

            Assert.Equal("текст", result.Corrected);
        }

        [Fact]
        public void A_dictionary_that_kept_the_text_vetoes_an_invented_answer()
        {
            var unchanged = new PhraseCorrection("guy;", "guy;", false, null);
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("ггнч"), new FakeAdviser(unchanged, Script.Cyrillic, Script.Latin));

            var result = rewriter.CorrectPhrase("guy;", Active, [Active], Candidates);

            Assert.False(result.Changed);
            Assert.Equal("guy;", result.Corrected);
        }

        [Fact]
        public void A_script_the_dictionary_cannot_read_leaves_the_model_in_charge()
        {
            var unchanged = new PhraseCorrection("gamarjoba", "gamarjoba", false, null);
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("გამარჯობა"), new FakeAdviser(unchanged, Script.Cyrillic, Script.Latin));

            var result = rewriter.CorrectPhrase("gamarjoba", Active, [Active], [.. Candidates, Georgian]);

            Assert.True(result.Changed);
            Assert.Equal("გამარჯობა", result.Corrected);
        }

        [Fact]
        public void An_answer_in_a_script_the_user_cannot_type_is_not_applied()
        {
            var hebrew = new LayoutSource(Script.Hebrew, "he", BundledKeyboardMaps.He, "he");
            LayoutCandidate[] candidates =
            [
                new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en"),
                new(Script.Hebrew, KeyboardLayout.He, BundledKeyboardMaps.He, "he", "he")
            ];
            var unchanged = new PhraseCorrection("מה הגוף", "מה הגוף", false, null);
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("מה العضوية"), new FakeAdviser(unchanged, Script.Hebrew, Script.Latin));

            var result = rewriter.CorrectPhrase("מה הגוף", hebrew, [hebrew], candidates);

            Assert.False(result.Changed);
            Assert.Equal("מה הגוף", result.Corrected);
        }

        [Fact]
        public void Without_an_adviser_a_translation_into_another_script_is_still_refused()
        {
            var rewriter = new NeuralPhraseRewriter(new FakeLlm("если важны"));
            var hebrew = new LayoutSource(Script.Hebrew, "he", BundledKeyboardMaps.He, "he");

            var result = rewriter.CorrectPhrase("ואם חשובים", hebrew, [hebrew], [Candidate(Script.Hebrew)]);

            Assert.False(result.Changed);
        }

        [Fact]
        public void A_remap_the_dictionary_reads_as_no_words_is_not_applied()
        {
            // On the phonetic Georgian layout the remap of correct English is its transliteration.
            var english = new PhraseCorrection("were that", "were that", false, null);
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("წერე ტჰატ"), new FakeAdviser(english, Script.Latin, Script.Georgian));

            var result = rewriter.CorrectPhrase("were that", Active, [Active], [Georgian]);

            Assert.False(result.Changed);
            Assert.Equal("were that", result.Corrected);
        }

        [Fact]
        public void A_remap_the_dictionary_knows_is_applied_though_it_kept_the_text()
        {
            var bulgarian = new LayoutCandidate(Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.Bg, "bg", "bg");
            var unchanged = new PhraseCorrection("sirena", "sirena", false, null);
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("сирена"),
                new FakeAdviser(unchanged, Script.Latin, Script.Cyrillic) { Words = ["сирена"] });

            var result = rewriter.CorrectPhrase("sirena", Active, [Active], [bulgarian]);

            Assert.True(result.Changed);
            Assert.Equal("сирена", result.Corrected);
        }

        [Fact]
        public void Without_an_adviser_the_raw_remap_is_still_the_fallback()
        {
            var rewriter = new NeuralPhraseRewriter(new FakeLlm("ntrcn"));

            var result = rewriter.CorrectPhrase("ntrcn", Active, [Active], Candidates, KeyboardLayout.Uk);

            Assert.Equal("текст", result.Corrected);
        }

        [Fact]
        public void An_adviser_with_no_dictionaries_loaded_is_ignored()
        {
            var advice = new PhraseCorrection("ntrcn", "wrong", true, null);
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("ntrcn"), new FakeAdviser(advice, ready: false));

            var result = rewriter.CorrectPhrase("ntrcn", Active, [Active], Candidates, KeyboardLayout.Uk);

            Assert.Equal("текст", result.Corrected);
        }

        [Fact]
        public void The_adviser_stands_in_while_no_model_is_loaded()
        {
            var advice = new PhraseCorrection("ntrcn", "текст", true, KeyboardLayout.Uk, "uk");
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("wrong") { Loaded = false }, new FakeAdviser(advice, Script.Cyrillic, Script.Latin));

            Assert.True(rewriter.IsReady);
            Assert.True(rewriter.SupportsLayoutCycle);
            Assert.True(rewriter.Knows(Script.Cyrillic));

            var result = rewriter.CorrectPhrase("ntrcn", Active, [Active], Candidates);
            Assert.Equal("текст", result.Corrected);
            Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
        }

        [Fact]
        public void A_loaded_model_offers_the_layout_circle_after_its_answer()
        {
            var advice = new PhraseCorrection("ntrcn", "текст", true, KeyboardLayout.Uk, "uk");
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("текст"), new FakeAdviser(advice, Script.Cyrillic, Script.Latin));

            Assert.True(rewriter.IsReady);
            Assert.True(rewriter.SupportsLayoutCycle);
        }

        [Fact]
        public void Without_an_adviser_an_unloaded_model_is_not_ready_and_leaves_the_text_alone()
        {
            var rewriter = new NeuralPhraseRewriter(new FakeLlm("текст") { Loaded = false });

            Assert.False(rewriter.IsReady);
            Assert.False(rewriter.CorrectPhrase("ntrcn", Active, [Active], Candidates).Changed);
        }

        private sealed class FakeAdviser(PhraseCorrection answer, params Script[] known) : IPhraseCorrector
        {
            private readonly bool _ready = true;

            public FakeAdviser(PhraseCorrection answer, bool ready) : this(answer)
            {
                _ready = ready;
            }

            public bool IsReady => _ready;

            public bool SupportsLayoutCycle => true;

            public bool PreferAsync => false;

            public string[] Words { get; init; } = [];

            public bool Knows(Script script) => Array.IndexOf(known, script) >= 0;

            public bool KnowsWord(string word, Script script) => Array.IndexOf(Words, word) >= 0;

            public PhraseCorrection CorrectPhrase(
                string text,
                LayoutSource active,
                IReadOnlyList<LayoutSource> installed,
                IReadOnlyList<LayoutCandidate> candidates,
                KeyboardLayout? preferred = null)
            {
                return answer;
            }
        }
    }

    public class WithAModelThatScores
    {
        private static readonly LayoutSource English = new(Script.Latin, "en", BundledKeyboardMaps.En, "en");

        private static readonly LayoutSource Russian = new(Script.Cyrillic, "ru", BundledKeyboardMaps.Ru, "ru");

        private static readonly LayoutCandidate[] Candidates =
        [
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en"),
            new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru", "ru")
        ];

        [Fact]
        public void Each_word_is_read_in_the_layout_it_was_meant_in()
        {
            var llm = new ScoringLlm("возьми", "данные", "из", "api");
            var rewriter = new NeuralPhraseRewriter(llm);

            var result = rewriter.CorrectPhrase("djpmvb lfyyst bp api", English, [English, Russian], Candidates);

            Assert.True(result.Changed);
            Assert.Equal("возьми данные из api", result.Corrected);
            Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
        }

        [Fact]
        public void A_slip_on_the_wrong_layout_is_switched_and_spell_fixed_together()
        {
            // n for the m beside it: the keys spell "возтми", the dictionary knows "возьми".
            var dictionary = new Dictionary(Script.Cyrillic, "возьми", "данные", "из")
            {
                Suggestions = { ["возтми"] = [("возьми", 1)] }
            };
            var rewriter = new NeuralPhraseRewriter(new ScoringLlm("возьми", "данные", "из", "api"), dictionary);

            var result = rewriter.CorrectPhrase("djpnvb lfyyst bp api", English, [English, Russian], Candidates);

            Assert.Equal("возьми данные из api", result.Corrected);
        }

        [Fact]
        public void A_text_too_long_for_one_window_is_read_a_sentence_at_a_time()
        {
            var llm = new ScoringLlm("возьми", "данные", "из", "api.");
            var rewriter = new NeuralPhraseRewriter(llm);
            var typed = string.Join(' ', Enumerable.Repeat("djpmvb lfyyst bp api.", 7));

            var result = rewriter.CorrectPhrase(typed, English, [English, Russian], Candidates);

            Assert.Equal(string.Join(' ', Enumerable.Repeat("возьми данные из api.", 7)), result.Corrected);
            Assert.InRange(llm.LongestText, 1, 24);
        }

        [Fact]
        public void The_arabic_lam_alef_is_offered_as_the_b_key_that_types_it()
        {
            var arabic = new LayoutSource(Script.Arabic, "ar", BundledKeyboardMaps.Ar, "ar");
            LayoutCandidate[] candidates =
            [
                new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en"),
                new(Script.Arabic, KeyboardLayout.Ar, BundledKeyboardMaps.Ar, "ar", "ar")
            ];
            var typed = LayoutTranscoder.Transcode("fix the bug", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar);
            var rewriter = new NeuralPhraseRewriter(new ScoringLlm("fix", "the", "bug"));

            var result = rewriter.CorrectPhrase(typed, arabic, [English, arabic], candidates);

            Assert.Equal("fix the bug", result.Corrected);
        }

        [Fact]
        public void A_word_spell_fixed_in_place_on_a_switched_line_may_still_switch()
        {
            // The dictionary moved "fix the" to English but read "لاعل" as an Arabic slip for "لاعب".
            var arabic = new LayoutSource(Script.Arabic, "ar", BundledKeyboardMaps.Ar, "ar");
            LayoutCandidate[] candidates =
            [
                new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en"),
                new(Script.Arabic, KeyboardLayout.Ar, BundledKeyboardMaps.Ar, "ar", "ar")
            ];
            var typed = LayoutTranscoder.Transcode("fix the bug", BundledKeyboardMaps.En, BundledKeyboardMaps.Ar);
            var dictionary = new Dictionary(Script.Arabic)
            {
                Answer = new PhraseCorrection(typed, "fix the لاعب", true, KeyboardLayout.En, "en")
            };
            var rewriter = new NeuralPhraseRewriter(new ScoringLlm("fix", "the", "bug"), dictionary);

            var result = rewriter.CorrectPhrase(typed, arabic, [English, arabic], candidates);

            Assert.Equal("fix the bug", result.Corrected);
        }

        [Fact]
        public void A_file_name_the_dictionary_respells_is_still_weighed_as_typed()
        {
            var dictionary = new Dictionary(Script.Cyrillic, "открой", "файл")
            {
                Answer = new PhraseCorrection("открой файл config.json", "открой файл config.son", true, null)
            };
            var rewriter = new NeuralPhraseRewriter(new ScoringLlm("открой", "файл", "config.json"), dictionary);

            var result = rewriter.CorrectPhrase("открой файл config.json", Russian, [English, Russian], Candidates);

            Assert.Equal("открой файл config.json", result.Corrected);
        }

        [Fact]
        public void A_line_the_model_reads_best_as_typed_stays()
        {
            var rewriter = new NeuralPhraseRewriter(new ScoringLlm("were", "that"));

            var result = rewriter.CorrectPhrase("were that", English, [English, Russian], Candidates);

            Assert.False(result.Changed);
            Assert.Equal("were that", result.Corrected);
        }

        [Fact]
        public void A_typo_takes_the_spelling_the_model_reads_in_context()
        {
            var dictionary = new Dictionary(Script.Cyrillic, "как", "дела", "привет")
            {
                Suggestions = { ["превет"] = [("привет", 1)] }
            };
            var rewriter = new NeuralPhraseRewriter(new ScoringLlm("привет", "как", "дела"), dictionary);

            var result = rewriter.CorrectPhrase("превет как дела", Russian, [English, Russian], Candidates);

            Assert.Equal("привет как дела", result.Corrected);
        }

        [Fact]
        public void A_listed_word_is_never_read_another_way()
        {
            var rewriter = new NeuralPhraseRewriter(
                new ScoringLlm("возьми", "данные", "из", "api"), exceptions: new WordExceptions(["bp"]));

            var result = rewriter.CorrectPhrase("djpmvb lfyyst bp api", English, [English, Russian], Candidates);

            Assert.Equal("возьми данные bp api", result.Corrected);
        }

        [Fact]
        public void A_line_the_dictionary_kept_and_knows_never_reaches_the_model()
        {
            var llm = new ScoringLlm("ye", "ys");
            var rewriter = new NeuralPhraseRewriter(llm, new Dictionary(Script.Cyrillic, "ну", "ні", "є"));

            var result = rewriter.CorrectPhrase("ну ні є", Russian, [English, Russian], Candidates);

            Assert.False(result.Changed);
            Assert.Equal(0, llm.Calls);
        }

        [Fact]
        public void Where_the_model_reads_poorly_the_dictionary_spelling_stands_unasked()
        {
            var dictionary = new Dictionary(Script.Cyrillic, "как", "дела", "привет", "предмет")
            {
                Answer = new PhraseCorrection("превет как дела", "привет как дела", true, null),
                Suggestions = { ["превет"] = [("предмет", 1)] }
            };
            var llm = new ScoringLlm("предмет", "как", "дела");
            var rewriter = new NeuralPhraseRewriter(llm, dictionary, spellingLanguages: new HashSet<string> { "en" });

            var result = rewriter.CorrectPhrase("превет как дела", Russian, [English, Russian], Candidates);

            Assert.Equal("привет как дела", result.Corrected);
            Assert.Equal(0, llm.Calls);
        }

        [Fact]
        public void A_second_press_cancels_the_first_request_instead_of_waiting_it_out()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var rewriter = new NeuralPhraseRewriter(new StuckLlm());

            Assert.ThrowsAny<OperationCanceledException>(() => rewriter.CorrectPhrase(
                "djpmvb lfyyst", English, [English, Russian], Candidates, null,
                new CorrectionHints(Cancellation: cancelled.Token)));
        }

        [Fact]
        public void A_model_past_its_time_gives_way_to_the_dictionary()
        {
            var dictionary = new Dictionary(Script.Cyrillic)
            {
                Answer = new PhraseCorrection("djpmvb lfyyst", "возьми данные", true, KeyboardLayout.Ru, "ru")
            };
            var rewriter = new NeuralPhraseRewriter(new StuckLlm(), dictionary, budget: TimeSpan.FromMilliseconds(50));

            var result = rewriter.CorrectPhrase("djpmvb lfyyst", English, [English, Russian], Candidates);

            Assert.Equal("возьми данные", result.Corrected);
            Assert.Equal(KeyboardLayout.Ru, result.TargetLayout);
        }

        // Never answers until told to stop.
        private sealed class StuckLlm : ILocalLlm
        {
            public bool IsLoaded => true;

            public void Unload()
            {
            }

            public async Task<string> CompleteAsync(string systemPrompt, string userText, int answerChars,
                CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return "";
            }

            public async Task<IReadOnlyList<double>?> ScoreAsync(string context, IReadOnlyList<string> texts,
                bool ends, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            }

            public void Dispose()
            {
            }
        }

        // Scores a text by how many of its words are in the list: the "model" reads those as natural.
        private sealed class ScoringLlm(params string[] natural) : ILocalLlm
        {
            public int Calls { get; private set; }

            public int LongestText { get; private set; }

            public bool IsLoaded => true;

            public void Unload()
            {
            }

            public Task<string> CompleteAsync(string systemPrompt, string userText, int answerChars,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult("текст");
            }

            public Task<IReadOnlyList<double>?> ScoreAsync(string context, IReadOnlyList<string> texts, bool ends,
                CancellationToken cancellationToken = default)
            {
                Calls++;
                LongestText = Math.Max(LongestText,
                    texts.Max(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));
                IReadOnlyList<double> scores = texts
                    .Select(t => 10.0 * t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(natural.Contains))
                    .ToList();
                return Task.FromResult<IReadOnlyList<double>?>(scores);
            }

            public void Dispose()
            {
            }
        }

        private sealed class Dictionary(Script script, params string[] words) : IPhraseCorrector
        {
            public Dictionary<string, (string Word, int Edits)[]> Suggestions { get; } = [];

            public PhraseCorrection? Answer { get; init; }

            public bool IsReady => true;

            public bool SupportsLayoutCycle => true;

            public bool PreferAsync => false;

            public bool Knows(Script other) => other == script;

            public bool KnowsWord(string word, Script other) => other == script && words.Contains(word);

            public IReadOnlyList<(string Word, int Edits)> SpellSuggestions(string word, Script other)
            {
                return Suggestions.TryGetValue(word, out var found) ? found : [];
            }

            public PhraseCorrection CorrectPhrase(
                string text,
                LayoutSource active,
                IReadOnlyList<LayoutSource> installed,
                IReadOnlyList<LayoutCandidate> candidates,
                KeyboardLayout? preferred = null)
            {
                return Answer ?? new PhraseCorrection(text, text, false, null);
            }
        }
    }

    private sealed class FakeLlm(string reply) : ILocalLlm
    {
        public string LastUserPrompt { get; private set; } = "";

        public int LastAnswerChars { get; private set; } = -1;

        public bool Loaded { get; set; } = true;

        public bool IsLoaded => Loaded;

        public void Unload()
        {
        }

        public Task<string> CompleteAsync(string systemPrompt, string userText, int answerChars,
            CancellationToken cancellationToken = default)
        {
            LastUserPrompt = userText;
            LastAnswerChars = answerChars;
            return Task.FromResult(reply);
        }

        public void Dispose()
        {
        }
    }
}
