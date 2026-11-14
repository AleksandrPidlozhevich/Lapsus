using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests.Correction;

public class NeuralPhraseRewriterTests
{
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

    public class WithADictionaryAdviser
    {
        private static readonly LayoutSource Active = new(Script.Latin, "en", BundledKeyboardMaps.En, "en");

        private static readonly LayoutCandidate[] Candidates =
        [
            new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk", "uk"),
            new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en")
        ];

        [Fact]
        public void One_word_the_dictionary_repairs_never_reaches_the_model()
        {
            var advice = new PhraseCorrection("gjk,dfyyz", "полювання", true, KeyboardLayout.Uk, "uk");
            var llm = new FakeLlm();
            var rewriter = new NeuralPhraseRewriter(
                llm, new FakeAdviser(advice, Script.Latin, Script.Cyrillic));

            var result = rewriter.CorrectPhrase("gjk,dfyyz", Active, [Active], Candidates);

            Assert.Equal("полювання", result.Corrected);
            Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
            Assert.False(llm.Asked);
        }

        [Fact]
        public void The_trailing_space_of_a_typed_line_still_counts_as_one_word()
        {
            var advice = new PhraseCorrection("ghbdtn ", "привет ", true, KeyboardLayout.Ru, "ru");
            var llm = new FakeLlm();
            var rewriter = new NeuralPhraseRewriter(
                llm, new FakeAdviser(advice, Script.Latin, Script.Cyrillic));

            var result = rewriter.CorrectPhrase("ghbdtn ", Active, [Active], Candidates);

            Assert.Equal("привет ", result.Corrected);
            Assert.False(llm.Asked);
        }

        [Fact]
        public void A_model_that_cannot_score_gives_way_to_the_dictionary()
        {
            const string typed = "gjk,dfyyz yf rfxjr";
            var advice = new PhraseCorrection(typed, "полювання на качок", true, KeyboardLayout.Uk, "uk");
            var rewriter = new NeuralPhraseRewriter(new FakeLlm(), new FakeAdviser(advice, Script.Cyrillic));

            var result = rewriter.CorrectPhrase(typed, Active, [Active], Candidates);

            Assert.Equal("полювання на качок", result.Corrected);
            Assert.Equal(KeyboardLayout.Uk, result.TargetLayout);
        }

        [Fact]
        public void The_adviser_stands_in_while_no_model_is_loaded()
        {
            var advice = new PhraseCorrection("ntrcn", "текст", true, KeyboardLayout.Uk, "uk");
            var rewriter = new NeuralPhraseRewriter(
                new FakeLlm() { Loaded = false }, new FakeAdviser(advice, Script.Cyrillic, Script.Latin));

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
                new FakeLlm(), new FakeAdviser(advice, Script.Cyrillic, Script.Latin));

            Assert.True(rewriter.IsReady);
            Assert.True(rewriter.SupportsLayoutCycle);
        }

        [Fact]
        public void Without_an_adviser_an_unloaded_model_is_not_ready_and_leaves_the_text_alone()
        {
            var rewriter = new NeuralPhraseRewriter(new FakeLlm() { Loaded = false });

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

    // Cannot score, as a runtime without logits: ScoreAsync answers null.
    private sealed class FakeLlm : ILocalLlm
    {
        public bool Asked { get; private set; }

        public bool Loaded { get; set; } = true;

        public bool IsLoaded => Loaded;

        public void Unload()
        {
        }

        public Task<string> CompleteAsync(string systemPrompt, string userText, int answerChars,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("");
        }

        public Task<IReadOnlyList<double>?> ScoreAsync(string context, IReadOnlyList<string> texts, bool ends,
            CancellationToken cancellationToken = default)
        {
            Asked = true;
            return Task.FromResult<IReadOnlyList<double>?>(null);
        }

        public void Dispose()
        {
        }
    }
}
