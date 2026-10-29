using Avalonia.Threading;
using Lapsus.Core.Input;
using Lapsus.Settings;

namespace Lapsus.Tests;

// A dictation hotkey of modifiers alone (Ctrl+Win): a quick tap toggles, other keys mean another shortcut.
public class DictationChordTests
{
    private const HotkeyModifiers Ctrl = HotkeyModifiers.Control;
    private const HotkeyModifiers CtrlWin = HotkeyModifiers.Control | HotkeyModifiers.Meta;
    private const HotkeyModifiers None = HotkeyModifiers.None;

    private static (FakeInputBackend Backend, List<string> Events) Rig()
    {
        var backend = new FakeInputBackend(new ScriptedCorrector("a", "b")).Listening();
        backend.DictationHotkey = HotkeyCombo.Encode(0, CtrlWin);
        var events = new List<string>();
        backend.DictationHotkeyPressed += () => events.Add("down");
        backend.DictationHotkeyReleased += interrupted => events.Add(interrupted ? "other" : "up");
        return (backend, events);
    }

    [Fact]
    public void A_quick_tap_is_a_press_and_a_release()
    {
        var (backend, events) = Rig();

        backend.Modifiers(Ctrl, CtrlWin, Ctrl, None);

        Assert.Equal(["down", "up"], events);
    }

    [Fact]
    public void Another_key_with_the_chord_does_nothing()
    {
        var (backend, events) = Rig();

        backend.Modifiers(Ctrl, CtrlWin);
        backend.OtherKey();
        backend.Modifiers(Ctrl, None);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(events);
    }

    [Fact]
    public void Letting_go_of_a_third_modifier_does_not_count_as_the_chord()
    {
        var (backend, events) = Rig();

        backend.Modifiers(Ctrl, CtrlWin, CtrlWin | HotkeyModifiers.Shift, CtrlWin, Ctrl, None);

        Assert.Empty(events);
    }

    [Fact]
    public void The_default_dictation_hotkey_is_the_chord()
    {
        var hotkey = new AppSettings().DictationHotkey;
        Assert.Equal(0, HotkeyCombo.KeyOf(hotkey));
        Assert.True(HotkeyCombo.ModifiersOf(hotkey).HasFlag(Ctrl));
    }
}
