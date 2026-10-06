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

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Meta = 8
}

// What a key press is while a shortcut is being recorded: a modifier only changes what is held, a named key
// (F-keys, Pause, Insert...) may stand alone, a typing key needs a modifier so it cannot hijack typing.
public enum HotkeyKeyKind
{
    Modifier,
    Escape,
    Named,
    Typing
}

public enum HotkeyCaptureKind
{
    Held,
    Rejected,
    Captured,
    Cancelled
}

// Trigger is the encoded shortcut for Captured; Held is the modifier set so far, for live feedback.
public readonly record struct HotkeyCaptureEvent(HotkeyCaptureKind Kind, HotkeyModifiers Held, int Trigger);

// A key pressed with modifiers, packed above the plain key codes. The bits used here are free: the
// double-tap flag is 0x0200_0000 and MacHotkeys.CtrlOptionSpace is 0x0100_0031.
public static class HotkeyCombo
{
    private const int ModifierShift = 26;

    private const int ModifierMask = 0x3C00_0000;

    private const int KeyMask = 0xFFFF;

    public static int Encode(int key, HotkeyModifiers modifiers)
    {
        return modifiers == HotkeyModifiers.None ? key : key | ((int)modifiers << ModifierShift);
    }

    public static bool IsCombo(int trigger)
    {
        return (trigger & ModifierMask) != 0;
    }

    public static int KeyOf(int trigger)
    {
        return trigger & KeyMask;
    }

    public static HotkeyModifiers ModifiersOf(int trigger)
    {
        return (HotkeyModifiers)((trigger & ModifierMask) >> ModifierShift);
    }

    // A combo fires only with exactly its own modifiers held, so Ctrl+K never fires for Ctrl+Shift+K.
    public static bool Triggers(int trigger, int key, HotkeyModifiers held)
    {
        return IsCombo(trigger) && KeyOf(trigger) == key && ModifiersOf(trigger) == held;
    }
}
