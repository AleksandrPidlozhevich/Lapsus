using Lapsus.Core.Input;
using Xunit;

namespace Lapsus.Core.Tests;

public class HotkeyComboTests
{
    private const int MacCtrlOptionSpace = 0x0100_0031;

    [Fact]
    public void PlainKeyStaysPlain()
    {
        var plain = HotkeyCombo.Encode(0x7B, HotkeyModifiers.None);

        Assert.Equal(0x7B, plain);
        Assert.False(HotkeyCombo.IsCombo(plain));
    }

    [Theory]
    [InlineData(HotkeyModifiers.Control)]
    [InlineData(HotkeyModifiers.Shift)]
    [InlineData(HotkeyModifiers.Alt)]
    [InlineData(HotkeyModifiers.Meta)]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Meta)]
    public void ComboRoundTripsKeyAndModifiers(HotkeyModifiers modifiers)
    {
        var combo = HotkeyCombo.Encode(0x4B, modifiers);

        Assert.True(HotkeyCombo.IsCombo(combo));
        Assert.Equal(0x4B, HotkeyCombo.KeyOf(combo));
        Assert.Equal(modifiers, HotkeyCombo.ModifiersOf(combo));
    }

    [Fact]
    public void ComboNeverLooksLikeDoubleTapOrCtrlOptionSpace()
    {
        foreach (var modifiers in Enum.GetValues<HotkeyModifiers>())
        {
            if (modifiers == HotkeyModifiers.None)
                continue;

            var combo = HotkeyCombo.Encode(0x31, modifiers);

            Assert.False(HotkeyTriggers.IsDoubleTap(combo));
            Assert.NotEqual(MacCtrlOptionSpace, combo);
        }

        foreach (var modifier in Enum.GetValues<TapModifier>())
            Assert.False(HotkeyCombo.IsCombo(HotkeyTriggers.DoubleTap(modifier)));

        Assert.False(HotkeyCombo.IsCombo(MacCtrlOptionSpace));
    }

    [Fact]
    public void ComboFiresOnlyWithExactlyItsModifiers()
    {
        var ctrlShiftK = HotkeyCombo.Encode(0x4B, HotkeyModifiers.Control | HotkeyModifiers.Shift);

        Assert.True(HotkeyCombo.Triggers(ctrlShiftK, 0x4B, HotkeyModifiers.Control | HotkeyModifiers.Shift));
        Assert.False(HotkeyCombo.Triggers(ctrlShiftK, 0x4B, HotkeyModifiers.Control));
        Assert.False(HotkeyCombo.Triggers(ctrlShiftK, 0x4B, HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt));
        Assert.False(HotkeyCombo.Triggers(ctrlShiftK, 0x4C, HotkeyModifiers.Control | HotkeyModifiers.Shift));
    }

    [Fact]
    public void PlainTriggerNeverMatchesAsCombo()
    {
        Assert.False(HotkeyCombo.Triggers(0x7B, 0x7B, HotkeyModifiers.Control));
    }
}
