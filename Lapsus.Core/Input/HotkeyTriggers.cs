namespace Lapsus.Core.Input;

public enum TapModifier
{
    Shift = 0,
    Control = 1,
    Alt = 2,
    Command = 3
}

public static class HotkeyTriggers
{
    private const int DoubleTapFlag = 0x0200_0000;

    public const int DoubleShift = DoubleTapFlag | (int)TapModifier.Shift;
    public const int DoubleControl = DoubleTapFlag | (int)TapModifier.Control;
    public const int DoubleAlt = DoubleTapFlag | (int)TapModifier.Alt;
    public const int DoubleCommand = DoubleTapFlag | (int)TapModifier.Command;

    public static int DoubleTap(TapModifier modifier)
    {
        return DoubleTapFlag | (int)modifier;
    }

    public static bool IsDoubleTap(int hotkey)
    {
        return (hotkey & DoubleTapFlag) != 0;
    }
}
