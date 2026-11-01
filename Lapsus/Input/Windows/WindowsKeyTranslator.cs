using Lapsus.Core.Layout;
using System;
using System.Runtime.Versioning;
using System.Text;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Input;

[SupportedOSPlatform("windows")]
internal sealed class WindowsKeyTranslator
{
    private readonly byte[] _keyState = new byte[256];
    private readonly StringBuilder _buffer = new(8);
    private readonly char[] _translated = new char[8];

    private bool _deadKeyPending;

    private char _pendingAccent;

    public void Reset()
    {
        _deadKeyPending = false;
        _pendingAccent = '\0';
    }

    public ReadOnlySpan<char> Translate(uint vk, uint scanCode, IntPtr layout, bool shift, bool rightAlt)
    {
        return _translated.AsSpan(0, TranslateInto(vk, scanCode, layout, shift, rightAlt));
    }

    private int TranslateInto(uint vk, uint scanCode, IntPtr layout, bool shift, bool rightAlt)
    {
        _keyState[VK_SHIFT] = shift ? (byte)0x80 : (byte)0x00;
        _keyState[VK_CAPITAL] = (GetKeyState(VK_CAPITAL) & 1) != 0 ? (byte)0x01 : (byte)0x00;

        _keyState[VK_CONTROL] = rightAlt ? (byte)0x80 : (byte)0x00;
        _keyState[VK_MENU] = rightAlt ? (byte)0x80 : (byte)0x00;

        var sb = _buffer;
        sb.Clear();

        var rc = ToUnicodeEx(vk, scanCode, _keyState, sb, sb.Capacity, 0x4, layout);

        if (rc < 0)
        {
            _deadKeyPending = true;
            _pendingAccent = sb.Length > 0 ? sb[0] : '\0';
            return 0;
        }

        var afterDeadKey = _deadKeyPending;
        var accent = _pendingAccent;
        _deadKeyPending = false;
        _pendingAccent = '\0';

        if (rc == 0 || sb.Length == 0)
            return 0;

        if (rc == 1)
        {
            _translated[0] = afterDeadKey && Accented(sb[0], accent) is { } composed ? composed : sb[0];
            return 1;
        }

        var count = Math.Min(sb.Length, _translated.Length);
        if (afterDeadKey)
        {
            var letter = sb[count - 1];
            _translated[0] = Accented(letter, accent) ?? letter;
            return 1;
        }

        sb.CopyTo(0, _translated, 0, count);
        return count;
    }

    private static char? Accented(char letter, char accent)
    {
        return accent != '\0' && char.IsLetter(letter) && DeadKeys.CombiningMarks(accent) is { } marks
            ? DeadKeys.Compose(letter, marks)
            : null;
    }
}
