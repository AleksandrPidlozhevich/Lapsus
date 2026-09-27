using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Input;

[SupportedOSPlatform("windows")]
internal static class WindowsTextInjection
{

    internal static readonly UIntPtr Marker = unchecked((UIntPtr)0x4C50_5355);

    public static bool ReplaceTrailing(int count, string text)
    {
        if (count <= 0)
            return SendUnicodeAccepted(text);

        var inputs = new INPUT[count * 2 + text.Length * 2];
        for (var i = 0; i < count; i++)
        {
            inputs[i * 2] = KeyboardInput((ushort)VK_BACK, '\0', false);
            inputs[i * 2 + 1] = KeyboardInput((ushort)VK_BACK, '\0', true);
        }

        for (var i = 0; i < text.Length; i++)
        {
            inputs[count * 2 + i * 2] = KeyboardInput(0, text[i], false, true);
            inputs[count * 2 + i * 2 + 1] = KeyboardInput(0, text[i], true, true);
        }

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == (uint)inputs.Length;
    }

    private static bool SendUnicodeAccepted(string text)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        return SendUnicode(text) == (uint)(text.Length * 2);
    }

    public static uint SendUnicode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var inputs = new INPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = KeyboardInput(0, text[i], false, true);
            inputs[i * 2 + 1] = KeyboardInput(0, text[i], true, true);
        }

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static bool SendCtrlChord(byte vk)
    {
        INPUT[] inputs =
        [
            KeyboardInput(VK_SHIFT, '\0', true),
            KeyboardInput(VK_MENU, '\0', true),
            KeyboardInput(VK_LWIN, '\0', true),
            KeyboardInput(VK_RWIN, '\0', true),
            KeyboardInput(VK_CONTROL, '\0', false),
            KeyboardInput(vk, '\0', false),
            KeyboardInput(vk, '\0', true),
            KeyboardInput(VK_CONTROL, '\0', true)
        ];

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == (uint)inputs.Length;
    }

    private static INPUT KeyboardInput(ushort vk, char scan, bool keyUp, bool unicode = false)
    {
        uint flags = 0;
        if (unicode) flags |= KEYEVENTF_UNICODE;
        if (keyUp) flags |= KEYEVENTF_KEYUP;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = unicode ? (ushort)0 : vk,
                    wScan = unicode ? scan : (ushort)0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = Marker
                }
            }
        };
    }
}
