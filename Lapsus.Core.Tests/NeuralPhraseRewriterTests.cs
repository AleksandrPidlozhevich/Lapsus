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

            var result = rewriter.CorrectPhrase("geia", Active, [Active], [..Candidates, greek]);

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

            var result = rewriter.CorrectPhrase("gamarjoba", Active, [Active], Candidates);

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

            var result = rewriter.CorrectPhrase("gamarjoba", Active, [Active], Candidates);

            Assert.True(result.Changed);
            Assert.Equal("გამარჯობა", result.Corrected);
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
        public void A_loaded_model_gives_one_rewrite_and_no_circle()
        {
            var advice = new PhraseCorrection("ntrcn", "текст", true, KeyboardLayout.Uk, "uk");
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm("текст"), new FakeAdviser(advice, Script.Cyrillic, Script.Latin));

            Assert.True(rewriter.IsReady);
            Assert.False(rewriter.SupportsLayoutCycle);
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

            public bool Knows(Script script) => Array.IndexOf(known, script) >= 0;

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
