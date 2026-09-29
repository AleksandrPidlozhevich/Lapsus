using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Tests;

public sealed class CorrectionCycleTests : IDisposable
{
    private readonly string _enPath = WriteTemp("en", "hello 900", "text 800");
    private readonly string _ukPath = WriteTemp("uk", "привет 900");

    public void Dispose()
    {
        File.Delete(_enPath);
        File.Delete(_ukPath);
    }

    [Fact]
    public void Cycling_past_the_last_hypothesis_restores_the_typed_text()
    {
        var backend = new FakeInputBackend(new ScriptedCorrector("ghbdtn", "привет")).Listening();
        backend.Type("ghbdtn");

        backend.PressHotkey();
        Assert.Equal("привет", backend.Injected[^1].Text);

        backend.PressHotkey();
        Assert.Equal("ghbdtn", backend.Injected[^1].Text);
    }

    [Fact]
    public void Cycling_wraps_around_instead_of_stopping()
    {
        var backend = new FakeInputBackend(new ScriptedCorrector("ghbdtn", "привет")).Listening();
        backend.Type("ghbdtn");

        backend.PressHotkey();
        Assert.Equal("привет", backend.Injected[^1].Text);

        backend.PressHotkey();
        Assert.Equal("ghbdtn", backend.Injected[^1].Text);

        backend.PressHotkey();
        Assert.Equal("привет", backend.Injected[^1].Text);

        backend.PressHotkey();
        Assert.Equal("ghbdtn", backend.Injected[^1].Text);

    }

    [Fact]
    public void Wrapping_does_not_ask_the_brain_again()
    {
        var brain = new ScriptedCorrector("ghbdtn", "привет");
        var backend = new FakeInputBackend(brain).Listening();
        backend.Type("ghbdtn");

        backend.PressHotkey();
        backend.PressHotkey();
        backend.PressHotkey();

        Assert.Equal(1, brain.Calls);
        Assert.Equal("привет", backend.Injected[^1].Text);
    }

    [Fact]
    public void Each_press_switches_the_os_layout_in_the_same_circle()
    {
        var backend = new FakeInputBackend(new ScriptedCorrector("ghbdtn", "привет")).Listening();
        backend.Type("ghbdtn");

        backend.PressHotkey();
        Assert.Equal("uk-UA", backend.LayoutSwitches[^1].Id);

        backend.PressHotkey();
        Assert.Equal("en-US", backend.LayoutSwitches[^1].Id);

        backend.PressHotkey();
        Assert.Equal("uk-UA", backend.LayoutSwitches[^1].Id);
    }

    [Fact]
    public void The_typed_gibberish_comes_back_on_the_original_layout()
    {
        var backend = new FakeInputBackend(new PlainLayoutSwitcher()).Listening();
        backend.Type("xxxx");

        backend.PressHotkey();
        Assert.NotEqual("xxxx", backend.Injected[^1].Text);
        Assert.Equal("uk-UA", backend.LayoutSwitches[^1].Id);

        backend.PressHotkey();
        Assert.Equal("xxxx", backend.Injected[^1].Text);
        Assert.Equal("en-US", backend.LayoutSwitches[^1].Id);

        backend.PressHotkey();
        Assert.NotEqual("xxxx", backend.Injected[^1].Text);
        Assert.Equal("uk-UA", backend.LayoutSwitches[^1].Id);
    }

    [Fact]
    public void A_real_word_still_walks_the_other_layout_and_back()
    {
        var backend = new FakeInputBackend(RealBrain(new WordExceptions())).Listening();
        backend.Type("hello");

        backend.PressHotkey();
        Assert.NotEqual("hello", backend.Injected[^1].Text);

        backend.PressHotkey();
        Assert.Equal("hello", backend.Injected[^1].Text);
    }

    [Fact]
    public void An_in_place_typo_fix_is_the_first_step_before_other_layouts()
    {
        var backend = new FakeInputBackend(RealBrain(new WordExceptions())).Listening();
        backend.Type("helo");

        backend.PressHotkey();
        Assert.Equal("hello", backend.Injected[^1].Text);
        Assert.Equal("en-US", backend.LayoutSwitches[^1].Id);

        backend.PressHotkey();
        Assert.NotEqual("hello", backend.Injected[^1].Text);
        Assert.Equal("uk-UA", backend.LayoutSwitches[^1].Id);

        backend.PressHotkey();
        Assert.Equal("helo", backend.Injected[^1].Text);
        Assert.Equal("en-US", backend.LayoutSwitches[^1].Id);
    }

    [Theory]
    [InlineData(false, "helo ")]
    [InlineData(true, "hello ")]
    public void Auto_mode_fixes_a_typo_only_when_asked_to(bool fixTypos, string expected)
    {
        var backend = new FakeInputBackend(RealBrain(new WordExceptions())).Listening().Focused();
        backend.AutoMode = true;
        backend.AutoFixTypos = fixTypos;

        backend.Type("helo ");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var landed = backend.Injected.Count > 0 ? backend.Injected[^1].Text : "helo ";
        Assert.Equal(expected, landed);

        backend.DiscardLine();
        backend.Type("ghbdtn ");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("привет ", backend.Injected[^1].Text);
    }

    [Fact]
    public void A_phrase_with_a_leading_oem_letter_rewrites_the_whole_line()
    {
        var ukPath = WriteTemp("uk-phrase", "я 100", "хочу 100");
        try
        {
            var backend = new FakeInputBackend(new LayoutCorrector(
                new SpellChecker(
                [
                    new DictionarySource("en", _enPath, Script.Latin),
                    new DictionarySource("uk", ukPath, Script.Cyrillic)
                ]))).Listening();
            backend.Type("z [jxe");

            backend.PressHotkey();

            var injected = Assert.Single(backend.Injected);
            Assert.Equal(6, injected.Backspaces);
            Assert.Equal("я хочу", injected.Text);
        }
        finally
        {
            File.Delete(ukPath);
        }
    }

    [Fact]
    public void A_real_brain_wraps_the_same_way()
    {
        var backend = new FakeInputBackend(RealBrain(new WordExceptions())).Listening();
        backend.Type("ghbdtn");

        backend.PressHotkey();
        Assert.Equal("привет", backend.Injected[^1].Text);

        backend.PressHotkey();
        Assert.Equal("ghbdtn", backend.Injected[^1].Text);

        backend.PressHotkey();
        Assert.Equal("привет", backend.Injected[^1].Text);
    }

    [Fact]
    public void A_half_and_half_line_is_finished_in_the_script_it_was_started_in()
    {
        var backend = new FakeInputBackend(new PlainLayoutSwitcher()).Listening();
        backend.Type("вітання мир ntrcn");

        backend.PressHotkey();

        Assert.Equal("вітання мир текст", backend.Injected[^1].Text);
        Assert.Equal(KeyboardLayout.Uk, backend.LayoutSwitches[^1].Target);
    }

    [Fact]
    public void The_typed_text_is_one_press_away_on_a_half_and_half_line()
    {
        var backend = new FakeInputBackend(new PlainLayoutSwitcher()).Listening();
        backend.Type("вітання мир ntrcn");

        backend.PressHotkey();
        backend.PressHotkey();

        Assert.Equal("вітання мир ntrcn", backend.Injected[^1].Text);
    }

    [Fact]
    public void A_step_of_the_circle_leaves_the_half_already_in_that_script_alone()
    {
        var backend = new FakeInputBackend(new PlainLayoutSwitcher())
        {
            InstalledLayouts = [FakeInputBackend.En, FakeInputBackend.Uk, FakeInputBackend.Ru]
        }.Listening();

        backend.Type("вітання мир ntrcn");

        var seen = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            backend.PressHotkey();
            seen.Add(backend.Injected[^1].Text);
        }

        Assert.DoesNotContain("вытання мир текст", seen);
        Assert.Contains("вітання мир текст", seen);
        Assert.Contains("вітання мир ntrcn", seen);
    }

    [Fact]
    public void A_step_of_the_circle_leaves_a_word_that_is_already_right_alone()
    {
        var backend = new FakeInputBackend(RealBrain(new WordExceptions()))
        {
            InstalledLayouts = [FakeInputBackend.En, FakeInputBackend.Uk, FakeInputBackend.Ru]
        }.Listening();

        backend.Type("ghbdtn hello");

        var seen = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            backend.PressHotkey();
            seen.Add(backend.Injected[^1].Text);
        }

        Assert.All(seen, text => Assert.DoesNotContain("руддщ", text));
        Assert.Contains("привет hello", seen);
        Assert.Contains("ghbdtn hello", seen);
    }

    [Fact]
    public void The_circle_never_re_encodes_a_word_on_the_leave_alone_list()
    {
        var backend = new FakeInputBackend(RealBrain(new WordExceptions(["hello"])))
        {
            InstalledLayouts = [FakeInputBackend.En, FakeInputBackend.Uk, FakeInputBackend.Ru]
        }.Listening();

        backend.Type("ghbdtn hello");

        var seen = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            backend.PressHotkey();
            seen.Add(backend.Injected[^1].Text);
        }

        Assert.All(seen, text => Assert.Contains("hello", text));
    }

    [Fact]
    public void A_line_that_is_already_right_still_walks_the_other_layouts()
    {
        var backend = new FakeInputBackend(RealBrain(new WordExceptions()))
        {
            InstalledLayouts = [FakeInputBackend.En, FakeInputBackend.Uk, FakeInputBackend.Ru]
        }.Listening();

        backend.Type("привет hello");

        backend.PressHotkey();

        Assert.NotEmpty(backend.Injected);
        Assert.NotEqual("привет hello", backend.Injected[^1].Text);
    }

    [Fact]
    public void The_hotkey_reads_only_what_was_typed_after_the_last_correction()
    {
        var brain = new TableCorrector(("ghbdtn", "привет"), (" rfr", " как"));
        var backend = new FakeInputBackend(brain).Listening();

        backend.Type("ghbdtn");
        backend.PressHotkey();
        backend.Type(" rfr");
        backend.PressHotkey();

        Assert.Equal(["ghbdtn", " rfr"], brain.Asked);
        Assert.Equal((4, " как"), backend.Injected[^1]);
    }

    [Fact]
    public void A_correction_walked_back_to_the_typed_text_is_not_corrected_again()
    {
        var brain = new TableCorrector(("ghbdtn", "привет"), (" rfr", " как"));
        var backend = new FakeInputBackend(brain).Listening();

        backend.Type("ghbdtn");
        backend.PressHotkey();
        backend.PressHotkey();
        Assert.Equal("ghbdtn", backend.Injected[^1].Text);

        backend.Type(" rfr");
        backend.PressHotkey();

        Assert.Equal(["ghbdtn", " rfr"], brain.Asked);
        Assert.Equal((4, " как"), backend.Injected[^1]);
    }

    [Fact]
    public void Nothing_new_after_a_correction_is_nothing_to_correct()
    {
        var brain = new TableCorrector(("ghbdtn", "привет"));
        var backend = new FakeInputBackend(brain).Listening();

        backend.Type("ghbdtn");
        backend.PressHotkey();
        backend.Type(" ");
        backend.PressHotkey();

        Assert.Single(brain.Asked);
        Assert.Single(backend.Injected);
    }

    [Fact]
    public void Enter_after_a_correction_starts_the_next_line_fresh()
    {
        var brain = new TableCorrector(("ghbdtn", "привет"));
        var backend = new FakeInputBackend(brain).Listening();

        backend.Type("ghbdtn");
        backend.PressHotkey();
        backend.Type("\n");

        // Enter clears the committed prefix from the line above; the new line starts at the first key.
        backend.Type("ghbdtn");
        backend.PressHotkey();

        Assert.Equal(["ghbdtn", "ghbdtn"], brain.Asked);
        Assert.Equal((6, "привет"), backend.Injected[^1]);
    }

    [Fact]
    public void A_shorter_line_after_Enter_is_read_whole_and_does_not_throw()
    {
        var brain = new TableCorrector(("ghbdtn", "привет"), ("ab", "фи"));
        var backend = new FakeInputBackend(brain).Listening();

        backend.Type("ghbdtn");
        backend.PressHotkey();
        backend.Type("\n");
        backend.Type("ab");

        backend.PressHotkey();

        Assert.Equal("ab", brain.Asked[^1]);
        Assert.Equal((2, "фи"), backend.Injected[^1]);
    }

    [Fact]
    public void Backspacing_through_a_committed_correction_reopens_the_line()
    {
        var brain = new TableCorrector(("ghbdtn", "привет"), ("rfr", "как"));
        var backend = new FakeInputBackend(brain).Listening();

        backend.Type("ghbdtn");
        backend.PressHotkey();
        backend.Type(" ");
        for (var i = 0; i < "привет ".Length; i++)
            backend.Backspace();

        backend.Type("rfr");
        backend.PressHotkey();

        Assert.Equal("rfr", brain.Asked[^1]);
        Assert.Equal((3, "как"), backend.Injected[^1]);
    }

    [Fact]
    public void Short_words_that_are_also_english_hits_are_one_press_from_the_russian_line()
    {
        var enPath = WriteTemp("en-short", "vs 500", "d 500");
        var ruPath = WriteTemp("ru-short", "мы 900", "в 900", "комнате 300");
        try
        {
            var seen = WalkCircle(EnRuBrain(enPath, ruPath), "vs d rjvyfnt");

            Assert.Contains("мы в комнате", seen);
        }
        finally
        {
            File.Delete(enPath);
            File.Delete(ruPath);
        }
    }

    [Fact]
    public void Auto_mode_follows_the_line_into_the_script_it_has_crossed_into()
    {
        var (enPath, ruPath) = ShortWordLists();
        try
        {
            var backend = AutoCorrectingEnRu(EnRuBrain(enPath, ruPath));

            // Each Space applies its verdict before the next key, as on the real dispatcher.
            backend.Type("ghbdtn ");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            backend.Type("d ");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, backend.Injected.Count);
            Assert.Equal((2, "в "), backend.Injected[^1]);
        }
        finally
        {
            File.Delete(enPath);
            File.Delete(ruPath);
        }
    }

    [Fact]
    public void A_short_word_alone_is_still_left_to_the_hotkey()
    {
        var (enPath, ruPath) = ShortWordLists();
        try
        {
            var backend = AutoCorrectingEnRu(EnRuBrain(enPath, ruPath));

            backend.Type("d ");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Empty(backend.Injected);
        }
        finally
        {
            File.Delete(enPath);
            File.Delete(ruPath);
        }
    }

    [Fact]
    public void A_real_english_word_left_standing_ends_the_line_direction()
    {
        var (enPath, ruPath) = ShortWordLists();
        try
        {
            var backend = AutoCorrectingEnRu(EnRuBrain(enPath, ruPath));

            foreach (var chunk in new[] { "ghbdtn ", "hello ", "d " })
            {
                backend.Type(chunk);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal("привет ", Assert.Single(backend.Injected).Text);
        }
        finally
        {
            File.Delete(enPath);
            File.Delete(ruPath);
        }
    }

    [Fact]
    public void The_hotkey_ends_the_line_direction()
    {
        var (enPath, ruPath) = ShortWordLists();
        try
        {
            var backend = AutoCorrectingEnRu(EnRuBrain(enPath, ruPath));

            backend.Type("ghbdtn ");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            backend.PressHotkey();
            var pressed = backend.Injected.Count;

            backend.Type("d ");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(pressed, backend.Injected.Count);
        }
        finally
        {
            File.Delete(enPath);
            File.Delete(ruPath);
        }
    }

    private static (string EnPath, string RuPath) ShortWordLists()
    {
        return (WriteTemp("en-line", "hello 900", "d 40"), WriteTemp("ru-line", "привет 900", "в 9000"));
    }

    private static FakeInputBackend AutoCorrectingEnRu(IPhraseCorrector brain)
    {
        var backend = new FakeInputBackend(brain)
        {
            InstalledLayouts = [FakeInputBackend.En, FakeInputBackend.Ru]
        }.Listening().Focused();
        backend.AutoMode = true;
        return backend;
    }

    [Fact]
    public void Every_press_but_the_restore_changes_the_text()
    {
        var enPath = WriteTemp("en-mixed", "hello 900", "world 800");
        var ruPath = WriteTemp("ru-mixed", "привет 900");
        try
        {
            var seen = WalkCircle(EnRuBrain(enPath, ruPath), "hello world ghbdtn");

            Assert.Equal("hello world привет", seen[0]);
            for (var i = 1; i < seen.Count; i++)
                Assert.NotEqual(seen[i - 1], seen[i]);
        }
        finally
        {
            File.Delete(enPath);
            File.Delete(ruPath);
        }
    }

    private static List<string> WalkCircle(IPhraseCorrector brain, string typed)
    {
        var backend = new FakeInputBackend(brain)
        {
            InstalledLayouts = [FakeInputBackend.En, FakeInputBackend.Ru]
        }.Listening();
        backend.Type(typed);

        var seen = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            backend.PressHotkey();
            seen.Add(backend.Injected.Count > 0 ? backend.Injected[^1].Text : typed);
            if (seen[^1] == typed)
                break;
        }

        return seen;
    }

    private static LayoutCorrector EnRuBrain(string enPath, string ruPath)
    {
        return new LayoutCorrector(new SpellChecker(
        [
            new DictionarySource("en", enPath, Script.Latin),
            new DictionarySource("ru", ruPath, Script.Cyrillic)
        ]));
    }

    private LayoutCorrector RealBrain(WordExceptions exceptions)
    {
        return new LayoutCorrector(
            new SpellChecker(
            [
                new DictionarySource("en", _enPath, Script.Latin),
                new DictionarySource("uk", _ukPath, Script.Cyrillic)
            ]),
            exceptions: exceptions);
    }

    private static string WriteTemp(string tag, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{tag}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }
}
