using Avalonia.Threading;
using Lapsus.Core.Correction;

namespace Lapsus.Tests;

public class StaleLineStateTests
{
    [Fact]
    public void A_word_typed_after_a_discarded_line_is_a_fresh_word()
    {
        var backend = AutoCorrecting();
        backend.TypeOn(layoutToken: 1, "ab");

        backend.DiscardLine();

        backend.TypeOn(layoutToken: 2, "cfk ");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("car ", Assert.Single(backend.Injected).Text);
    }

    [Fact]
    public void Words_typed_before_the_first_correction_runs_are_each_corrected()
    {
        var backend = AutoCorrecting();
        backend.Type("cfk cfk ");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("car car ", ScreenAfter(backend, "cfk cfk "));
    }

    [Fact]
    public void A_word_corrected_after_more_was_typed_keeps_what_followed_it()
    {
        var backend = AutoCorrecting();
        backend.Type("cfk x");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("car x", ScreenAfter(backend, "cfk x"));
    }

    // Replays the injections over the text the field held, as the app's keystrokes and corrections would.
    private static string ScreenAfter(FakeInputBackend backend, string typed)
    {
        var screen = new System.Text.StringBuilder(typed);
        foreach (var (backspaces, text) in backend.Injected)
        {
            screen.Remove(screen.Length - backspaces, backspaces);
            screen.Append(text);
        }

        return screen.ToString();
    }

    [Fact]
    public void A_layout_switch_inside_a_word_still_suppresses_auto_mode()
    {
        var backend = AutoCorrecting();

        backend.TypeOn(layoutToken: 1, "cf");
        backend.TypeOn(layoutToken: 2, "k ");
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(backend.Injected);
    }

    [Fact]
    public void A_separator_ends_the_verdict_with_the_word()
    {
        var backend = AutoCorrecting();

        backend.TypeOn(layoutToken: 1, "ab ");
        backend.TypeOn(layoutToken: 2, "cfk ");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("car ", Assert.Single(backend.Injected).Text);
    }

    [Fact]
    public void Backspacing_a_word_away_starts_the_next_one_fresh()
    {
        var backend = AutoCorrecting();
        backend.TypeOn(layoutToken: 1, "ab");

        backend.Backspace();
        backend.Backspace();

        backend.TypeOn(layoutToken: 2, "cfk ");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("car ", Assert.Single(backend.Injected).Text);
    }

    [Fact]
    public void Backspacing_back_into_a_word_puts_us_back_inside_it()
    {
        var backend = AutoCorrecting();
        backend.TypeOn(layoutToken: 1, "cf ");

        backend.Backspace();

        backend.TypeOn(layoutToken: 2, "k ");
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(backend.Injected);
    }

    [Fact]
    public void A_word_deleting_backspace_drops_the_line_instead_of_one_character()
    {
        var backend = new FakeInputBackend(new ScriptedCorrector("ghbdtn", "привет")).Listening();
        backend.Type("hello world");

        // Ctrl+Backspace: the field lost "world"; the buffer must not still hold "worl".
        backend.Backspace(withModifier: true);
        backend.Type("ghbdtn");
        backend.PressHotkey();

        Assert.Equal((6, "привет"), Assert.Single(backend.Injected));
    }

    [Fact]
    public void A_neural_rewrite_lands_when_the_line_is_untouched()
    {
        var backend = RunNeuralRewrite(whileRunning: _ => { });

        Assert.Equal((6, "привет"), Assert.Single(backend.Injected));
        Assert.False(backend.SwitchSystemLayout);
        Assert.Equal("uk-UA", Assert.Single(backend.LayoutSwitches).Id);
    }

    [Fact]
    public void Auto_mode_corrects_a_word_with_the_punctuation_glued_to_it()
    {
        var backend = AutoCorrecting(new TableCorrector(("pyf.", "знаю")));

        backend.Type("pyf. ");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal((5, "знаю "), Assert.Single(backend.Injected));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Auto_mode_switches_the_layout_only_when_the_setting_asks(bool switchLayout)
    {
        var backend = AutoCorrecting(new TableCorrector(("cfk", "car")));
        backend.SwitchSystemLayout = switchLayout;

        backend.Type("cfk ");
        Dispatcher.UIThread.RunJobs();

        Assert.Single(backend.Injected);
        Assert.Equal(switchLayout ? 1 : 0, backend.LayoutSwitches.Count);
    }

    [Fact]
    public void A_word_auto_mode_corrected_is_not_read_again_by_the_hotkey()
    {
        var brain = new TableCorrector(("cfk", "car"));
        var backend = AutoCorrecting(brain);

        backend.Type("cfk ");
        Dispatcher.UIThread.RunJobs();
        backend.Type("ntcn");
        backend.PressHotkey();

        Assert.Equal("ntcn", brain.Asked[^1]);
    }

    [Fact]
    public void The_hotkey_right_after_auto_mode_walks_the_word_alone_back_to_how_it_was_typed()
    {
        var backend = AutoCorrecting(new TableCorrector(("cfk", "car")));

        backend.Type("hello cfk ");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((4, "car "), backend.Injected[^1]);

        backend.PressHotkey();
        Assert.Equal((4, "сал "), backend.Injected[^1]);

        backend.PressHotkey();
        Assert.Equal((4, "cfk "), backend.Injected[^1]);
    }

    [Fact]
    public void The_circle_after_auto_mode_follows_the_brain_like_the_hotkeys_own()
    {
        var backend = AutoCorrecting(new ScriptedCorrector("cfk", "car"));

        backend.Type("cfk ");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((4, "car "), backend.Injected[^1]);

        backend.PressHotkey();
        Assert.Equal((4, "cfk "), backend.Injected[^1]);
    }

    [Fact]
    public void Auto_mode_never_fires_on_a_brain_that_cannot_decline()
    {
        foreach (var brain in new IPhraseCorrector[] { new PlainLayoutSwitcher(), new InactiveCorrector() })
        {
            var backend = AutoCorrecting(brain);

            backend.Type("cfk ");
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(backend.Injected);
        }
    }

    [Fact]
    public void A_layout_switch_before_glued_punctuation_still_suppresses_auto_mode()
    {
        var backend = AutoCorrecting(new TableCorrector(("cfk.", "car.")));

        backend.TypeOn(layoutToken: 1, "cfk");
        backend.TypeOn(layoutToken: 2, ". ");
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(backend.Injected);
    }

    [Fact]
    public void Enter_after_an_auto_correction_starts_the_next_line_fresh()
    {
        var brain = new TableCorrector(("cfk", "car"), ("ntcn", "test"));
        var backend = AutoCorrecting(brain);

        backend.Type("cfk ");
        Dispatcher.UIThread.RunJobs();
        backend.Type("\n");

        backend.Type("ntcn");
        backend.PressHotkey();

        Assert.Equal("ntcn", brain.Asked[^1]);
        Assert.Equal((4, "test"), backend.Injected[^1]);
    }

    [Fact]
    public void Enter_after_a_neural_rewrite_starts_the_next_line_fresh()
    {
        var backend = RunNeuralRewrite(whileRunning: _ => { }, afterwards: (b, _) =>
        {
            b.Type("\n");
            b.Type("ghbdtn");
            b.PressHotkey();
        });

        Assert.Equal(2, backend.Injected.Count);
        Assert.Equal((6, "привет"), backend.Injected[^1]);
    }

    [Fact]
    public void A_second_press_after_a_neural_rewrite_restores_the_typed_text_without_asking_again()
    {
        var calls = 0;
        var backend = RunNeuralRewrite(whileRunning: _ => { }, afterwards: (b, brain) =>
        {
            b.PressHotkey();
            calls = brain.Calls;
        });

        Assert.Equal(1, calls);
        Assert.Equal((6, "ghbdtn"), backend.Injected[^1]);
        Assert.Equal("en-US", backend.LayoutSwitches[^1].Id);
    }

    [Fact]
    public void A_third_press_after_a_neural_rewrite_wraps_to_the_rewrite_without_asking_again()
    {
        var calls = 0;
        var backend = RunNeuralRewrite(whileRunning: _ => { }, afterwards: (b, brain) =>
        {
            b.PressHotkey();
            b.PressHotkey();
            calls = brain.Calls;
        });

        Assert.Equal(1, calls);
        Assert.Equal((6, "привет"), backend.Injected[^1]);
        Assert.Equal("uk-UA", backend.LayoutSwitches[^1].Id);
    }

    [Fact]
    public void Typing_after_a_neural_rewrite_commits_it()
    {
        var calls = 0;
        var backend = RunNeuralRewrite(whileRunning: _ => { }, afterwards: (b, brain) =>
        {
            b.Type(" rfr");
            b.PressHotkey();
            Assert.True(b.PendingNeuralRewrite!.Wait(TimeSpan.FromSeconds(5)));
            calls = brain.Calls;
        });

        Assert.Equal(2, calls);
        Assert.Single(backend.Injected);
    }

    [Fact]
    public void A_neural_rewrite_is_dropped_when_the_line_was_cleared_while_it_ran()
    {
        // Enter while the model thinks: backspaces would eat the line break and the paragraph above.
        var backend = RunNeuralRewrite(whileRunning: b => b.Type("\n"));

        Assert.Empty(backend.Injected);
    }

    [Fact]
    public void A_neural_rewrite_is_dropped_when_the_line_was_retyped_the_same_while_it_ran()
    {
        var backend = RunNeuralRewrite(whileRunning: b =>
        {
            b.DiscardLine();
            b.Type("ghbdtn");
        });

        Assert.Empty(backend.Injected);
    }

    [Fact]
    public void A_second_press_cancels_the_model_request_still_running_for_the_first()
    {
        WithoutSyncContext(() =>
        {
            using var brain = new CancellableAsyncCorrector();
            var backend = new FakeInputBackend(brain).Listening().Focused();
            backend.Type("ghbdtn");

            backend.PressHotkey();
            Assert.True(brain.Entered.Wait(TimeSpan.FromSeconds(5)));
            backend.PressHotkey();
            Assert.True(brain.Entered.Wait(TimeSpan.FromSeconds(5)));

            Assert.True(brain.Tokens[0].IsCancellationRequested);
            Assert.False(brain.Tokens[1].IsCancellationRequested);
            Assert.Empty(backend.Injected);

            backend.SetCorrector(new ScriptedCorrector("x", "y"));
            Assert.True(brain.Tokens[1].IsCancellationRequested);
            Assert.True(backend.PendingNeuralRewrite!.Wait(TimeSpan.FromSeconds(5)));
        });
    }

    private static void WithoutSyncContext(Action body)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            body();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static FakeInputBackend RunNeuralRewrite(
        Action<FakeInputBackend> whileRunning, Action<FakeInputBackend, GatedAsyncCorrector>? afterwards = null)
    {
        // No sync context: the rewrite's continuation must not wait on a thread this test is blocking.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            using var brain = new GatedAsyncCorrector("ghbdtn", "привет");
            var backend = new FakeInputBackend(brain).Listening().Focused();
            backend.Type("ghbdtn");

            backend.PressHotkey();
            Assert.True(brain.Entered.Wait(TimeSpan.FromSeconds(5)));

            whileRunning(backend);
            brain.Release.Set();

            Assert.True(backend.PendingNeuralRewrite!.Wait(TimeSpan.FromSeconds(5)));

            afterwards?.Invoke(backend, brain);
            if (afterwards is not null)
                Assert.True(backend.PendingNeuralRewrite!.Wait(TimeSpan.FromSeconds(5)));

            return backend;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static FakeInputBackend AutoCorrecting(IPhraseCorrector? brain = null)
    {
        var backend = new FakeInputBackend(brain ?? new ScriptedCorrector("cfk", "car")).Listening().Focused();
        backend.AutoMode = true;
        return backend;
    }
}
