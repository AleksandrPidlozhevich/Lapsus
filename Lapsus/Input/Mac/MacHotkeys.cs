using Lapsus.Core.Input;
using System.Runtime.Versioning;

namespace Lapsus.Input;

internal static class MacHotkeys
{

    public const int SpaceKeyCode = 0x31;

    // Control-Option-Space. Also the system "next input source" shortcut, so it is not offered
    // as the correction hotkey. Kept so a settings file that still stores it can be recognized.
    public const int CtrlOptionSpace = 0x0100_0031;

    [SupportedOSPlatform("macos")]
    public static HotkeyModifiers HeldModifiersOf(ulong flags)
    {
        var held = HotkeyModifiers.None;
        if ((flags & MacOSNativeMethods.EventFlagMaskShift) != 0)
            held |= HotkeyModifiers.Shift;
        if ((flags & MacOSNativeMethods.EventFlagMaskControl) != 0)
            held |= HotkeyModifiers.Control;
        if ((flags & MacOSNativeMethods.EventFlagMaskAlternate) != 0)
            held |= HotkeyModifiers.Alt;
        if ((flags & MacOSNativeMethods.EventFlagMaskCommand) != 0)
            held |= HotkeyModifiers.Meta;

        return held;
    }

    [SupportedOSPlatform("macos")]
    public static HotkeyKeyKind KeyKindOf(ushort keyCode)
    {
        if (ModifierOf(keyCode) is not null)
            return HotkeyKeyKind.Modifier;

        if (keyCode == MacOSNativeMethods.EscapeKeyCode)
            return HotkeyKeyKind.Escape;

        var named = keyCode is 0x39 or 0x72 or 0x73 or 0x74 or 0x75 or 0x77 or 0x79
            or 0x7A or 0x78 or 0x63 or 0x76 or 0x60 or 0x61 or 0x62 or 0x64 or 0x65 or 0x6D or 0x67 or 0x6F
            or 0x69 or 0x6B or 0x71 or 0x6A or 0x40 or 0x4F or 0x50 or 0x5A;
        return named ? HotkeyKeyKind.Named : HotkeyKeyKind.Typing;
    }

    [SupportedOSPlatform("macos")]
    public static TapModifier? ModifierOf(ushort keyCode)
    {
        return keyCode switch
        {
            MacOSNativeMethods.ShiftKeyCode or MacOSNativeMethods.RightShiftKeyCode => TapModifier.Shift,
            MacOSNativeMethods.ControlKeyCode or MacOSNativeMethods.RightControlKeyCode => TapModifier.Control,
            MacOSNativeMethods.OptionKeyCode or MacOSNativeMethods.RightOptionKeyCode => TapModifier.Alt,
            MacOSNativeMethods.CommandKeyCode or MacOSNativeMethods.RightCommandKeyCode => TapModifier.Command,
            _ => null
        };
    }

    [SupportedOSPlatform("macos")]
    public static ulong FlagMaskOf(TapModifier modifier)
    {
        return modifier switch
        {
            TapModifier.Shift => MacOSNativeMethods.EventFlagMaskShift,
            TapModifier.Control => MacOSNativeMethods.EventFlagMaskControl,
            TapModifier.Alt => MacOSNativeMethods.EventFlagMaskAlternate,
            _ => MacOSNativeMethods.EventFlagMaskCommand
        };
    }

    [SupportedOSPlatform("macos")]
    public static bool HasCtrlOption(ulong flags)
    {
        return (flags & MacOSNativeMethods.EventFlagMaskControl) != 0
               && (flags & MacOSNativeMethods.EventFlagMaskAlternate) != 0
               && (flags & MacOSNativeMethods.EventFlagMaskCommand) == 0;
    }

    [SupportedOSPlatform("macos")]
    public static bool IsCaretMovingKey(ushort keyCode)
    {
        return keyCode is
            MacOSNativeMethods.TabKeyCode or MacOSNativeMethods.EscapeKeyCode
            or MacOSNativeMethods.HomeKeyCode or MacOSNativeMethods.EndKeyCode
            or MacOSNativeMethods.PageUpKeyCode or MacOSNativeMethods.PageDownKeyCode
            or MacOSNativeMethods.LeftArrowKeyCode or MacOSNativeMethods.RightArrowKeyCode
            or MacOSNativeMethods.UpArrowKeyCode or MacOSNativeMethods.DownArrowKeyCode
            or MacOSNativeMethods.ForwardDeleteKeyCode;
    }

    [SupportedOSPlatform("macos")]
    public static bool HasMeaningfulModifiers(ulong flags)
    {
        return (flags & (MacOSNativeMethods.EventFlagMaskControl
                         | MacOSNativeMethods.EventFlagMaskAlternate
                         | MacOSNativeMethods.EventFlagMaskCommand)) != 0;
    }
}
