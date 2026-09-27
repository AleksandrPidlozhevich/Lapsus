using Lapsus.Core.Correction;

namespace Lapsus.Tests;

public sealed class PlainSwitchHotkeyTests
{
    [Fact]
    public void The_hotkey_converts_the_line_with_no_dictionary_loaded()
    {
        var backend = new FakeInputBackend(new PlainLayoutSwitcher()).Listening();
        backend.Type("ghbdtn");

        backend.PressHotkey();

        Assert.Equal("привет", backend.Injected[^1].Text);
    }

    [Fact]
    public void A_second_press_puts_the_typed_text_back()
    {
        var backend = new FakeInputBackend(new PlainLayoutSwitcher()).Listening();
        backend.Type("ghbdtn");

        backend.PressHotkey();
        backend.PressHotkey();

        Assert.Equal("ghbdtn", backend.Injected[^1].Text);
    }

    [Fact]
    public void It_converts_a_real_word_as_readily_as_keystrokes()
    {
        var backend = new FakeInputBackend(new PlainLayoutSwitcher()).Listening();
        backend.Type("hello");

        backend.PressHotkey();

        Assert.Equal("руддщ", backend.Injected[^1].Text);
    }
}
