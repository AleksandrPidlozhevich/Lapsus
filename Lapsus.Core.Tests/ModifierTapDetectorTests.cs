using Lapsus.Core.Input;

namespace Lapsus.Core.Tests;

public class ModifierTapDetectorTests
{
    private readonly ModifierTapDetector _detector = new();

    private TapModifier? Tap(TapModifier modifier, long downAt, long upAt)
    {
        _detector.Note(modifier, true, downAt);
        return _detector.Note(modifier, false, upAt);
    }

    [Fact]
    public void Two_quick_taps_fire()
    {
        Assert.Null(Tap(TapModifier.Shift, 0, 50));
        Assert.Equal(TapModifier.Shift, Tap(TapModifier.Shift, 150, 200));
    }

    [Fact]
    public void One_tap_does_not_fire()
    {
        Assert.Null(Tap(TapModifier.Shift, 0, 50));
    }

    [Fact]
    public void Taps_too_far_apart_do_not_fire()
    {
        Assert.Null(Tap(TapModifier.Shift, 0, 50));
        Assert.Null(Tap(TapModifier.Shift, 50 + ModifierTapDetector.GapMs + 1, 500));
    }

    [Fact]
    public void Gap_is_measured_to_the_second_press()
    {
        Assert.Null(Tap(TapModifier.Shift, 0, 50));
        Assert.Equal(TapModifier.Shift, Tap(TapModifier.Shift, 340, 500));
    }

    [Fact]
    public void Different_modifiers_do_not_pair_up()
    {
        Assert.Null(Tap(TapModifier.Shift, 0, 50));
        Assert.Null(Tap(TapModifier.Control, 100, 150));
    }

    [Fact]
    public void A_long_hold_is_not_a_tap()
    {
        Assert.Null(Tap(TapModifier.Shift, 0, ModifierTapDetector.MaxHoldMs + 1));
        Assert.Null(Tap(TapModifier.Shift, 100 + ModifierTapDetector.MaxHoldMs, 150 + ModifierTapDetector.MaxHoldMs));
    }

    [Fact]
    public void Shift_plus_a_letter_is_not_a_tap()
    {
        _detector.Note(TapModifier.Shift, true, 0);
        _detector.NoteOtherInput();
        Assert.Null(_detector.Note(TapModifier.Shift, false, 50));

        Assert.Null(Tap(TapModifier.Shift, 100, 150));
    }

    [Fact]
    public void Typing_two_capitals_does_not_fire()
    {
        _detector.Note(TapModifier.Shift, true, 0);
        _detector.NoteOtherInput();
        _detector.Note(TapModifier.Shift, false, 40);

        _detector.NoteOtherInput();

        _detector.Note(TapModifier.Shift, true, 120);
        _detector.NoteOtherInput();
        Assert.Null(_detector.Note(TapModifier.Shift, false, 160));
    }

    [Fact]
    public void A_key_between_the_taps_cancels()
    {
        Assert.Null(Tap(TapModifier.Shift, 0, 50));
        _detector.NoteOtherInput();
        Assert.Null(Tap(TapModifier.Shift, 100, 150));
    }

    [Fact]
    public void A_second_modifier_held_alongside_cancels()
    {
        _detector.Note(TapModifier.Shift, true, 0);
        _detector.Note(TapModifier.Control, true, 10);
        _detector.Note(TapModifier.Control, false, 40);
        Assert.Null(_detector.Note(TapModifier.Shift, false, 50));

        Assert.Null(Tap(TapModifier.Shift, 100, 150));
    }

    [Fact]
    public void Auto_repeat_does_not_restart_the_hold()
    {
        Assert.Null(Tap(TapModifier.Shift, 0, 50));

        _detector.Note(TapModifier.Shift, true, 150);
        _detector.Note(TapModifier.Shift, true, 180);
        Assert.Equal(TapModifier.Shift, _detector.Note(TapModifier.Shift, false, 200));
    }

    [Fact]
    public void A_third_tap_does_not_fire_again()
    {
        Tap(TapModifier.Shift, 0, 50);
        Assert.Equal(TapModifier.Shift, Tap(TapModifier.Shift, 100, 150));
        Assert.Null(Tap(TapModifier.Shift, 200, 250));
        Assert.Equal(TapModifier.Shift, Tap(TapModifier.Shift, 300, 350));
    }

    [Fact]
    public void Reset_forgets_a_pending_tap()
    {
        Tap(TapModifier.Shift, 0, 50);
        _detector.Reset();
        Assert.Null(Tap(TapModifier.Shift, 100, 150));
    }

    [Fact]
    public void An_unmatched_release_clears_the_history()
    {
        Assert.Null(Tap(TapModifier.Shift, 0, 50));
        Assert.Null(_detector.Note(TapModifier.Shift, false, 60));
        Assert.Null(Tap(TapModifier.Shift, 100, 150));
    }

    [Fact]
    public void Trigger_values_round_trip_and_do_not_collide_with_key_codes()
    {
        foreach (var modifier in Enum.GetValues<TapModifier>())
        {
            var trigger = HotkeyTriggers.DoubleTap(modifier);
            Assert.True(HotkeyTriggers.IsDoubleTap(trigger));
            Assert.True(trigger > 0xFFFF);
        }

        Assert.False(HotkeyTriggers.IsDoubleTap(0x13));
        Assert.False(HotkeyTriggers.IsDoubleTap(0x14));
        Assert.False(HotkeyTriggers.IsDoubleTap(0));
    }
}
