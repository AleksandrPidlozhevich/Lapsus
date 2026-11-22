using System;
using System.Runtime.Versioning;

namespace Lapsus.Input;

// One copy of the current input source, read once per keystroke and released after it.
[SupportedOSPlatform("macos")]
internal readonly struct MacKeyboardSource : IDisposable
{
    private const uint ShiftModifierKeyState = 0x02;

    private const uint AlphaLockModifierKeyState = 0x04;

    private readonly IntPtr _source;

    private MacKeyboardSource(IntPtr source)
    {
        _source = source;
    }

    public static MacKeyboardSource Current() => new(MacOSNativeMethods.TISCopyCurrentKeyboardInputSource());

    public bool IsValid => _source != IntPtr.Zero;

    private IntPtr LayoutData => IsValid
        ? MacOSNativeMethods.TISGetInputSourceProperty(_source, MacOSNativeMethods.PropertyUnicodeKeyLayoutData)
        : IntPtr.Zero;

    // An IME (Japanese, Chinese, ...) publishes no Unicode key layout.
    public bool IsInputMethod => IsValid && LayoutData == IntPtr.Zero;

    // Tells layouts apart without reading the id as a string.
    public long Token
    {
        get
        {
            if (!IsValid)
                return 0;

            var id = MacOSNativeMethods.TISGetInputSourceProperty(_source, MacOSNativeMethods.PropertyInputSourceId);
            return id == IntPtr.Zero ? 0 : MacOSNativeMethods.CFHash(id);
        }
    }

    public string? Id => IsValid ? MacInstalledLayouts.ReadInputSourceId(_source) : null;

    public bool TranslateKeyDown(
        ushort keyCode, ulong eventFlags, bool autoRepeat, ref uint deadKeyState, char[] buffer, out int length)
    {
        length = 0;
        var layoutData = LayoutData;
        if (layoutData == IntPtr.Zero)
            return false;

        var modifiers = ((eventFlags & MacOSNativeMethods.EventFlagMaskShift) != 0 ? ShiftModifierKeyState : 0)
                        | ((eventFlags & MacOSNativeMethods.EventFlagMaskAlphaShift) != 0 ? AlphaLockModifierKeyState : 0);
        var state = deadKeyState;
        nuint charCount = 0;
        var status = MacOSNativeMethods.UCKeyTranslate(
            MacOSNativeMethods.CFDataGetBytePtr(layoutData),
            keyCode,
            autoRepeat ? MacOSNativeMethods.UCKeyActionAutoKey : MacOSNativeMethods.UCKeyActionDown,
            modifiers,
            MacOSNativeMethods.LMGetKbdType(),
            0,
            ref state,
            (nuint)buffer.Length,
            ref charCount,
            buffer);
        if (status != 0)
            return false;

        deadKeyState = state;
        length = (int)charCount;
        return true;
    }

    public void Dispose()
    {
        if (_source != IntPtr.Zero)
            MacOSNativeMethods.CFRelease(_source);
    }
}
