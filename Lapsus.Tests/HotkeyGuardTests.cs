using Avalonia.Threading;
using Lapsus.Core.Input;

namespace Lapsus.Tests;

public class HotkeyGuardTests
{
    [Fact]
    public void The_guard_is_asked_on_every_press()
    {
        var backend = new FakeInputBackend(new ScriptedCorrector("ghbdtn", "привет")).Listening();
        backend.Type("ghbdtn");

        backend.PressHotkey();
        backend.PressHotkey();

        Assert.Equal(2, backend.BlockedReasonAsked);
    }

    [Fact]
    public void A_blocked_press_reports_the_reason_and_writes_nothing()
    {
        var backend = new FakeInputBackend(new ScriptedCorrector("ghbdtn", "привет")).Listening();
        backend.Type("ghbdtn");
        backend.BlockedReason = "focus moved";

        backend.PressHotkey();

        Assert.Equal(1, backend.BlockedReasonAsked);
        Assert.Empty(backend.Injected);
        Assert.Equal(["focus moved"], backend.Diagnostics);
    }

    [Fact]
    public void A_double_tapped_modifier_meets_the_same_guard()
    {
        var backend = new FakeInputBackend(new ScriptedCorrector("ghbdtn", "привет")).Listening();
        backend.HotkeyVirtualKey = HotkeyTriggers.DoubleShift;
        backend.Type("ghbdtn");
        backend.BlockedReason = "focus moved";

        backend.DoubleTap(TapModifier.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, backend.BlockedReasonAsked);
        Assert.Empty(backend.Injected);
        Assert.Equal(["focus moved"], backend.Diagnostics);
    }

    [Fact]
    public void A_double_tapped_modifier_corrects_when_nothing_blocks_it()
    {
        var backend = new FakeInputBackend(new ScriptedCorrector("ghbdtn", "привет")).Listening();
        backend.HotkeyVirtualKey = HotkeyTriggers.DoubleShift;
        backend.Type("ghbdtn");

        backend.DoubleTap(TapModifier.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("привет", Assert.Single(backend.Injected).Text);
    }

    [Fact]
    public void The_guard_runs_before_the_buffer_is_even_looked_at()
    {
        var backend = new FakeInputBackend(new ScriptedCorrector("ghbdtn", "привет")).Listening();
        backend.BlockedReason = "password field";

        backend.PressHotkey();

        Assert.Equal(["password field"], backend.Diagnostics);
    }
}
