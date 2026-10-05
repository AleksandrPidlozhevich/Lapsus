using Lapsus.Core.Layout;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal static class MacInstalledLayouts
{
    private static readonly ushort[] KeyCodes =
    [
        0x00, 0x0B, 0x08, 0x02, 0x0E, 0x03, 0x05, 0x04, 0x22, 0x26, 0x28, 0x25, 0x2E, 0x2D, 0x1F, 0x23,
        0x0C, 0x0F, 0x01, 0x11, 0x20, 0x09, 0x0D, 0x07, 0x10, 0x06,
        0x1D, 0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19,
        0x29, 0x18, 0x2B, 0x1B, 0x2F, 0x2C, 0x32, 0x21, 0x2A, 0x1E, 0x27, 0x0A
    ];

    private const uint ShiftModifierKeyState = 0x02;

    private const uint AlphaLockModifierKeyState = 0x04;

    public static IReadOnlyList<InstalledLayout> Enumerate()
    {
        var list = MacOSNativeMethods.TISCreateInputSourceList(IntPtr.Zero, false);
        if (list == IntPtr.Zero)
            return Array.Empty<InstalledLayout>();

        try
        {
            var count = MacOSNativeMethods.CFArrayGetCount(list);
            var result = new List<InstalledLayout>((int)count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (long i = 0; i < count; i++)
            {
                var source = MacOSNativeMethods.CFArrayGetValueAtIndex(list, i);
                var id = ReadInputSourceId(source);
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    continue;

                var deadKeys = new List<DeadKey>();
                var ligatures = new List<Ligature>();
                var slots = BuildSlots(source, false, deadKeys, ligatures);
                if (slots is null)
                    continue;

                if (TryKnownMacLayoutSlots(id, out var knownSlots))
                    slots = knownSlots;

                var script = Scripts.Dominant(slots);
                var osCode = LayoutLanguageResolver.FromMacInputSourceId(id);
                var (languageCode, target) = LayoutLanguageResolver.Resolve(script, slots, osCode);

                var shiftedSlots = BuildSlots(source, true, deadKeys, ligatures);
                var shifted = shiftedSlots is null || (script is { } s && Scripts.IsCaseless(s))
                    ? shiftedSlots
                    : KeyboardMap.ShiftedSymbolsOnly(slots, shiftedSlots);

                result.Add(new InstalledLayout(
                    id, IntPtr.Zero, script, languageCode, target,
                    new KeyboardMap(slots, shifted, script is Script.Latin or null ? null : deadKeys, ligatures)));
            }

            return result;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(list);
        }
    }

    public static string? ReadInputSourceId(IntPtr source)
    {
        var value = MacOSNativeMethods.TISGetInputSourceProperty(
            source, MacOSNativeMethods.PropertyInputSourceId);
        if (value == IntPtr.Zero)
            return null;

        var direct = MacOSNativeMethods.CFStringGetCStringPtr(value, MacOSNativeMethods.CFStringEncodingUtf8);
        if (direct != IntPtr.Zero)
            return Marshal.PtrToStringUTF8(direct);

        var sb = new StringBuilder(256);
        if (!MacOSNativeMethods.CFStringGetCString(
                value, sb, sb.Capacity, MacOSNativeMethods.CFStringEncodingUtf8))
            return null;

        return sb.ToString();
    }

    public static string? CurrentLanguageCode()
    {
        var source = MacOSNativeMethods.TISCopyCurrentKeyboardInputSource();
        if (source == IntPtr.Zero)
            return null;

        try
        {
            var id = ReadInputSourceId(source);
            if (LayoutLanguageResolver.FromMacInputSourceId(id) is { } known)
                return known;

            return FirstDeclaredLanguage(source);
        }
        finally
        {
            MacOSNativeMethods.CFRelease(source);
        }
    }

    private static string? FirstDeclaredLanguage(IntPtr source)
    {
        var languages = MacOSNativeMethods.TISGetInputSourceProperty(
            source, MacOSNativeMethods.PropertyInputSourceLanguages);
        if (languages == IntPtr.Zero)
            return null;

        if (MacOSNativeMethods.CFArrayGetCount(languages) <= 0)
            return null;

        var raw = MacOSNativeMethods.ReadCfString(
            MacOSNativeMethods.CFArrayGetValueAtIndex(languages, 0));
        if (raw is not { Length: >= 2 })
            return null;

        var code = raw[..2].ToLowerInvariant();
        return code is { Length: 2 } and not "iv" ? code : null;
    }

    public static bool CurrentSourceIsInputMethod()
    {
        return ReadCurrentSource(out var isInputMethod) && isInputMethod;
    }

    public static bool ReadCurrentSource(out bool isInputMethod, out long token)
    {
        isInputMethod = false;
        token = 0;

        var source = MacOSNativeMethods.TISCopyCurrentKeyboardInputSource();
        if (source == IntPtr.Zero)
            return false;

        try
        {
            isInputMethod = MacOSNativeMethods.TISGetInputSourceProperty(
                source, MacOSNativeMethods.PropertyUnicodeKeyLayoutData) == IntPtr.Zero;

            var id = MacOSNativeMethods.TISGetInputSourceProperty(
                source, MacOSNativeMethods.PropertyInputSourceId);
            token = id == IntPtr.Zero ? 0 : MacOSNativeMethods.CFHash(id);
            return true;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(source);
        }
    }

    private static bool ReadCurrentSource(out bool isInputMethod)
    {
        return ReadCurrentSource(out isInputMethod, out _);
    }

    // One key-down through the current layout, dead-key state carried across calls as the OS carries it.
    public static bool TranslateKeyDown(
        ushort keyCode, ulong eventFlags, bool autoRepeat, ref uint deadKeyState, char[] buffer, out int length)
    {
        length = 0;

        var source = MacOSNativeMethods.TISCopyCurrentKeyboardInputSource();
        if (source == IntPtr.Zero)
            return false;

        try
        {
            var layoutData = MacOSNativeMethods.TISGetInputSourceProperty(
                source, MacOSNativeMethods.PropertyUnicodeKeyLayoutData);
            if (layoutData == IntPtr.Zero)
                return false;

            var modifiers = ((eventFlags & MacOSNativeMethods.EventFlagMaskShift) != 0 ? ShiftModifierKeyState : 0)
                            | ((eventFlags & MacOSNativeMethods.EventFlagMaskAlphaShift) != 0 ? AlphaLockModifierKeyState : 0);
            var state = deadKeyState;
            ushort charCount = 0;
            var status = MacOSNativeMethods.UCKeyTranslate(
                MacOSNativeMethods.CFDataGetBytePtr(layoutData),
                keyCode,
                autoRepeat ? MacOSNativeMethods.UCKeyActionAutoKey : MacOSNativeMethods.UCKeyActionDown,
                modifiers,
                MacOSNativeMethods.LMGetKbdType(),
                0,
                ref state,
                (uint)buffer.Length,
                ref charCount,
                buffer);
            if (status != 0)
                return false;

            deadKeyState = state;
            length = charCount;
            return true;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(source);
        }
    }

    public static IntPtr ResolveInputSource(string layoutId)
    {
        var list = MacOSNativeMethods.TISCreateInputSourceList(IntPtr.Zero, false);
        if (list == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            var count = MacOSNativeMethods.CFArrayGetCount(list);
            for (long i = 0; i < count; i++)
            {
                var source = MacOSNativeMethods.CFArrayGetValueAtIndex(list, i);
                if (!string.Equals(ReadInputSourceId(source), layoutId, StringComparison.Ordinal))
                    continue;

                MacOSNativeMethods.CFRetain(source);
                return source;
            }
        }
        finally
        {
            MacOSNativeMethods.CFRelease(list);
        }

        return IntPtr.Zero;
    }

    private static bool TryKnownMacLayoutSlots(string sourceId, out char[] slots)
    {
        slots = [];
        var id = sourceId.ToLowerInvariant();

        if (IsPhoneticOrQwerty(id))
            return false;

        if (id.Contains("ukrainian"))
        {
            var appleVowels = !id.Contains("legacy") && !id.Contains("pc");
            slots = BuildKnownMacJcukenSlots(MacJcukenKind.Ukrainian, appleVowels);
            return true;
        }

        if (id.Contains("belarus"))
        {
            slots = BuildKnownMacJcukenSlots(MacJcukenKind.Belarusian, false);
            return true;
        }

        if (id.Contains("russian"))
        {
            slots = BuildKnownMacJcukenSlots(MacJcukenKind.Russian, false);
            return true;
        }

        return false;
    }

    private static bool IsPhoneticOrQwerty(string id)
    {
        return id.Contains("qwerty") || id.Contains("phonetic") || id.Contains("mnemonic");
    }

    private enum MacJcukenKind
    {
        Russian,
        Ukrainian,
        Belarusian
    }

    private static char[] BuildKnownMacJcukenSlots(MacJcukenKind kind, bool appleVowels)
    {
        var slots = new char[KeyCodes.Length];
        for (var i = 0; i < KeyCodes.Length; i++)
            slots[i] = CharForKnownMacKey(KeyCodes[i], kind, appleVowels);
        return slots;
    }

    private static char CharForKnownMacKey(ushort keyCode, MacJcukenKind kind, bool appleVowels)
    {
        var ukrainian = kind == MacJcukenKind.Ukrainian;
        var belarusian = kind == MacJcukenKind.Belarusian;

        var bee = kind switch
        {
            MacJcukenKind.Ukrainian when appleVowels => 'і',
            MacJcukenKind.Ukrainian => 'и',
            MacJcukenKind.Belarusian => 'і',
            _ => 'и'
        };
        var ess = kind switch
        {
            MacJcukenKind.Ukrainian when appleVowels => 'и',
            MacJcukenKind.Ukrainian => 'і',
            _ => 'ы'
        };
        var ooh = belarusian ? 'ў' : 'щ';
        var bracketRight = ukrainian ? 'ї' : belarusian ? '\'' : 'ъ';
        var quote = ukrainian ? 'є' : 'э';
        var grave = ukrainian ? '\'' : 'ё';

        return keyCode switch
        {
            0x00 => 'ф',
            0x0B => bee,
            0x08 => 'с',
            0x02 => 'в',
            0x0E => 'у',
            0x03 => 'а',
            0x05 => 'п',
            0x04 => 'р',
            0x22 => 'ш',
            0x26 => 'о',
            0x28 => 'л',
            0x25 => 'д',
            0x2E => 'ь',
            0x2D => 'т',
            0x1F => ooh,
            0x23 => 'з',
            0x0C => 'й',
            0x0F => 'к',
            0x01 => ess,
            0x11 => 'е',
            0x20 => 'г',
            0x09 => 'м',
            0x0D => 'ц',
            0x07 => 'ч',
            0x10 => 'н',
            0x06 => 'я',

            0x1D => '0',
            0x12 => '1',
            0x13 => '2',
            0x14 => '3',
            0x15 => '4',
            0x17 => '5',
            0x16 => '6',
            0x1A => '7',
            0x1C => '8',
            0x19 => '9',

            0x29 => 'ж',
            0x18 => '=',
            0x2B => 'б',
            0x1B => '-',
            0x2F => 'ю',
            0x2C => '.',
            0x32 => grave,
            0x21 => 'х',

            0x2A => ukrainian && !appleVowels ? 'ґ' : '\\',
            0x1E => bracketRight,
            0x27 => quote,

            _ => '\0'
        };
    }

    private static char[]? BuildSlots(
        IntPtr inputSource, bool shifted, List<DeadKey> deadKeys, List<Ligature> ligatures)
    {
        var layoutData = MacOSNativeMethods.TISGetInputSourceProperty(
            inputSource, MacOSNativeMethods.PropertyUnicodeKeyLayoutData);
        if (layoutData == IntPtr.Zero)
            return null;

        var length = MacOSNativeMethods.CFDataGetLength(layoutData);
        if (length <= 0)
            return null;

        var bytes = new byte[length];
        Marshal.Copy(MacOSNativeMethods.CFDataGetBytePtr(layoutData), bytes, 0, (int)length);

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var layoutPtr = handle.AddrOfPinnedObject();
            var keyboardType = MacOSNativeMethods.LMGetKbdType();
            var slots = new char[KeyCodes.Length];

            for (var i = 0; i < KeyCodes.Length; i++)
            {
                var buffer = new char[4];
                uint deadKeyState = 0;
                ushort charCount = 0;
                var status = MacOSNativeMethods.UCKeyTranslate(
                    layoutPtr,
                    KeyCodes[i],
                    MacOSNativeMethods.UCKeyActionDisplay,
                    shifted ? ShiftModifierKeyState : 0,
                    keyboardType,
                    MacOSNativeMethods.UCKeyTranslateNoDeadKeys,
                    ref deadKeyState,
                    (uint)buffer.Length,
                    ref charCount,
                    buffer);

                if (status != 0 || charCount == 0)
                    continue;

                // With NoDeadKeys a dead key displays its accent; it owns no character, as on Windows.
                if (IsDeadKey(layoutPtr, keyboardType, KeyCodes[i], shifted))
                {
                    deadKeys.Add(new DeadKey(i, shifted, buffer[0]));
                    continue;
                }

                if (charCount > 1)
                {
                    ligatures.Add(new Ligature(i, shifted, new string(buffer, 0, charCount)));
                    continue;
                }

                slots[i] = shifted ? buffer[0] : char.ToLowerInvariant(buffer[0]);
            }

            return slots;
        }
        finally
        {
            handle.Free();
        }
    }

    private static bool IsDeadKey(IntPtr layoutPtr, uint keyboardType, ushort keyCode, bool shifted)
    {
        var buffer = new char[4];
        uint deadKeyState = 0;
        ushort charCount = 0;

        var status = MacOSNativeMethods.UCKeyTranslate(
            layoutPtr,
            keyCode,
            MacOSNativeMethods.UCKeyActionDown,
            shifted ? ShiftModifierKeyState : 0,
            keyboardType,
            0,
            ref deadKeyState,
            (uint)buffer.Length,
            ref charCount,
            buffer);

        return status == 0 && charCount == 0 && deadKeyState != 0;
    }
}
