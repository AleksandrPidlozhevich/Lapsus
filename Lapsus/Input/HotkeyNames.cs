using Lapsus.Core.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lapsus.Input;

// Labels for recorded shortcuts, in the same style as the built-in options: symbols on macOS, words elsewhere.
internal static class HotkeyNames
{
    public static string Format(int trigger)
    {
        var key = HotkeyCombo.KeyOf(trigger);
        var modifiers = HotkeyCombo.ModifiersOf(trigger);

        if (OperatingSystem.IsMacOS())
            return string.Concat(ModifierLabels(modifiers)) + MacKeyName(key);

        return string.Join('+', ModifierLabels(modifiers).Append(WindowsKeyName(key)));
    }

    // The modifiers held right now, shown as chips while a shortcut is being recorded.
    public static IReadOnlyList<string> ModifierLabels(HotkeyModifiers modifiers)
    {
        var labels = new List<string>(4);
        if (OperatingSystem.IsMacOS())
        {
            if (modifiers.HasFlag(HotkeyModifiers.Control)) labels.Add("⌃");
            if (modifiers.HasFlag(HotkeyModifiers.Alt)) labels.Add("⌥");
            if (modifiers.HasFlag(HotkeyModifiers.Shift)) labels.Add("⇧");
            if (modifiers.HasFlag(HotkeyModifiers.Meta)) labels.Add("⌘");
        }
        else
        {
            if (modifiers.HasFlag(HotkeyModifiers.Control)) labels.Add("Ctrl");
            if (modifiers.HasFlag(HotkeyModifiers.Alt)) labels.Add("Alt");
            if (modifiers.HasFlag(HotkeyModifiers.Shift)) labels.Add("Shift");
            if (modifiers.HasFlag(HotkeyModifiers.Meta)) labels.Add("Win");
        }

        return labels;
    }

    private static string WindowsKeyName(int key)
    {
        return key switch
        {
            >= 0x30 and <= 0x39 => ((char)key).ToString(),
            >= 0x41 and <= 0x5A => ((char)key).ToString(),
            >= 0x70 and <= 0x87 => "F" + (key - 0x6F),
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x13 => "Pause / Break",
            0x14 => "Caps Lock",
            0x91 => "Scroll Lock",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x25 => "←",
            0x26 => "↑",
            0x27 => "→",
            0x28 => "↓",
            _ => $"Key {key:X2}"
        };
    }

    // Virtual keycodes as macOS reports them (ANSI layout), which is what the event tap sees.
    private static string MacKeyName(int key)
    {
        return key switch
        {
            0x00 => "A", 0x0B => "B", 0x08 => "C", 0x02 => "D", 0x0E => "E", 0x03 => "F", 0x05 => "G",
            0x04 => "H", 0x22 => "I", 0x26 => "J", 0x28 => "K", 0x25 => "L", 0x2E => "M", 0x2D => "N",
            0x1F => "O", 0x23 => "P", 0x0C => "Q", 0x0F => "R", 0x01 => "S", 0x11 => "T", 0x20 => "U",
            0x09 => "V", 0x0D => "W", 0x07 => "X", 0x10 => "Y", 0x06 => "Z",
            0x12 => "1", 0x13 => "2", 0x14 => "3", 0x15 => "4", 0x17 => "5", 0x16 => "6", 0x1A => "7",
            0x1C => "8", 0x19 => "9", 0x1D => "0",
            0x31 => "Space", 0x30 => "Tab", 0x24 => "Return", 0x33 => "Delete", 0x35 => "Esc",
            0x39 => "Caps Lock", 0x75 => "Forward Delete",
            0x7A => "F1", 0x78 => "F2", 0x63 => "F3", 0x76 => "F4", 0x60 => "F5", 0x61 => "F6",
            0x62 => "F7", 0x64 => "F8", 0x65 => "F9", 0x6D => "F10", 0x67 => "F11", 0x6F => "F12",
            0x72 => "Help", 0x73 => "Home", 0x74 => "Page Up", 0x77 => "End", 0x79 => "Page Down",
            0x7B => "←", 0x7C => "→", 0x7E => "↑", 0x7D => "↓",
            _ => $"Key {key:X2}"
        };
    }
}
