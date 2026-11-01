using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Core.Text;
using Lapsus.Input.Dictation;
using Lapsus.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input;

// IDictationHost: where dictated text goes and the dictation hotkey events.
internal abstract partial class InputBackendBase<TFocus>
{
    // ---- Dictation (IDictationHost) ----

    public int DictationHotkey { get; set; }

    // Read on the key hook's thread: Esc cancels a recording while this is true.
    public bool DictationListening { get; set; }

    public event Action? DictationHotkeyPressed;

    public event Action<bool>? DictationHotkeyReleased;

    public event Action? DictationCancelPressed;

    protected void RaiseDictationHotkey() => DictationHotkeyPressed?.Invoke();

    protected void RaiseDictationReleased(bool interrupted = false) => DictationHotkeyReleased?.Invoke(interrupted);

    protected void RaiseDictationCancel() => DictationCancelPressed?.Invoke();

    // UI thread: every modifier going down or up, with the whole set held now. True when this completed the chord.
    protected bool NoteDictationModifiers(HotkeyModifiers held) => _dictationChord.NoteModifiers(held);

    // UI thread: a key other than a modifier went down.
    protected void NoteDictationOtherKey() => _dictationChord.NoteOtherKey();


    // No password check: dictation reads nothing from the field, only types into it, and the check fails
    // closed while it runs (hundreds of ms in Chrome/Electron), which refused dictation in ordinary fields.
    public string? DictationBlockedReason() => InputBlockedReason(passwords: false);

    // The field with focus right now; a platform that can read it says so, the rest fall back to the line's owner.
    protected virtual TFocus CurrentFocus() => BufferOwner;

    public object CaptureDictationTarget() => CurrentFocus();

    // A field nobody could read at the start (the Mac App Store sandbox has no Accessibility) cannot be
    // checked later either; the text then goes wherever the caret is, as the typed keys would.
    public bool IsDictationTargetFocused(object target) =>
        target is TFocus focus && (focus.IsEmpty || IsStillFocused(focus));

    public bool InsertDictatedText(string text)
    {
        if (text.Length == 0)
            return false;

        // A word typed right before the caret gets a space between it and the dictation.
        var line = _buffer.Segment;
        var spaced = line.Length > 0 && !char.IsWhiteSpace(line[^1]) && !char.IsWhiteSpace(text[0])
            ? " " + text
            : text;
        if (!TryInject(0, spaced))
            return false;

        // The dictated text becomes the line, committed, so the next dictation knows a word precedes the caret
        // and the correction hotkey does not take it for something typed on the wrong layout.
        ResetTypingBuffer();
        foreach (var ch in spaced)
            _buffer.Append(ch);
        _committedLength = _buffer.Length;
        return true;
    }

    public bool CopyDictatedText(string text) => SetClipboardText(text);

    protected virtual bool SetClipboardText(string text) => false;

    public string? ActiveLanguage() => ActiveInstalledLayout()?.LanguageCode;

    public IReadOnlyList<string> InstalledLanguages()
    {
        var languages = new List<string>();
        foreach (var layout in Layouts ??= EnumerateLayouts())
            if (layout.LanguageCode is { Length: > 0 } code && !languages.Contains(code))
                languages.Add(code);
        return languages;
    }

    public string DictationAppName() => ForegroundAppName();

    public void ReportDictation(string message) => RaiseDiagnostic(message);
}
