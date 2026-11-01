using Lapsus.Core.Input;
using System.Runtime.Versioning;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Input;

// Which modifiers are held, as the low-level keyboard hook saw them go down and up.
[SupportedOSPlatform("windows")]
internal sealed class WindowsModifierKeys
{
    public bool Shift { get; private set; }

    public bool RightAlt { get; private set; }

    public bool Win { get; private set; }

    private bool _ctrl;
    private bool _alt;

    // AltGr types a character; it is not a shortcut modifier.
    public bool IsShortcutDown => (_ctrl || _alt || Win) && !RightAlt;

    public void Reset()
    {
        Shift = _ctrl = _alt = RightAlt = Win = false;
    }

    public void Track(uint vk, bool isDown, bool isUp)
    {
        switch (vk)
        {
            case VK_SHIFT or VK_LSHIFT or VK_RSHIFT:
                if (isDown) Shift = true;
                else if (isUp) Shift = false;
                break;
            case VK_CONTROL or VK_LCONTROL or VK_RCONTROL:
                if (isDown) _ctrl = true;
                else if (isUp) _ctrl = false;
                break;
            case VK_MENU or VK_LMENU:
                if (isDown) _alt = true;
                else if (isUp) _alt = false;
                break;
            case VK_RMENU:
                if (isDown) _alt = RightAlt = true;
                else if (isUp) _alt = RightAlt = false;
                break;
            case VK_LWIN or VK_RWIN:
                if (isDown) Win = true;
                else if (isUp) Win = false;
                break;
        }
    }

    public HotkeyModifiers Held()
    {
        var held = HotkeyModifiers.None;
        if (Shift)
            held |= HotkeyModifiers.Shift;

        // AltGr arrives as Ctrl with RightAlt; that Ctrl was not pressed by the user.
        if (_ctrl && !RightAlt)
            held |= HotkeyModifiers.Control;
        if (_alt)
            held |= HotkeyModifiers.Alt;
        if (Win)
            held |= HotkeyModifiers.Meta;

        return held;
    }

    public static HotkeyKeyKind KeyKindOf(uint vk)
    {
        if (ModifierOf(vk) is not null || vk is VK_LWIN or VK_RWIN)
            return HotkeyKeyKind.Modifier;

        if (vk == VK_ESCAPE)
            return HotkeyKeyKind.Escape;

        var named = vk is 0x13 or 0x14 or 0x91 or 0x2D or 0x2E or 0x24 or 0x23 or 0x21 or 0x22
                    or (>= 0x70 and <= 0x87);
        return named ? HotkeyKeyKind.Named : HotkeyKeyKind.Typing;
    }

    public static TapModifier? ModifierOf(uint vk)
    {
        return vk switch
        {
            VK_SHIFT or VK_LSHIFT or VK_RSHIFT => TapModifier.Shift,
            VK_CONTROL or VK_LCONTROL or VK_RCONTROL => TapModifier.Control,
            VK_MENU or VK_LMENU or VK_RMENU => TapModifier.Alt,
            _ => null
        };
    }

    public static bool IsCaretMovingKey(uint vk)
    {
        return vk is VK_TAB or VK_ESCAPE
            or VK_PRIOR or VK_NEXT or VK_END or VK_HOME
            or VK_LEFT or VK_UP or VK_RIGHT or VK_DOWN
            or VK_INSERT or VK_DELETE;
    }
}
