using System;
using System.Collections.Generic;

namespace Lapsus.Input.Dictation;

// Every member is called on the UI thread, and both events are raised there.
internal interface IDictationHost
{
    // 0 turns the hotkey off. Same encoding as every other hotkey (plain key, combo, modifier double tap), plus
    // modifiers alone (Ctrl+Win, ⌃⌥: a combo whose key is 0), which are meant to be held.
    int DictationHotkey { get; set; }

    // While true the hook takes Esc for itself (it cancels the recording) instead of passing it on.
    bool DictationListening { get; set; }

    // A double tap raises both this and the release at once.
    event Action? DictationHotkeyPressed;

    // True: another key went down while the hotkey was held, so the press was some other shortcut.
    event Action<bool>? DictationHotkeyReleased;

    event Action? DictationCancelPressed;

    string? DictationBlockedReason();

    // An opaque token for the field that has focus now.
    object CaptureDictationTarget();

    bool IsDictationTargetFocused(object target);

    // Types the text at the caret, after a space when the caret follows a word typed in this field.
    bool InsertDictatedText(string text);

    // Where the text goes when its field lost focus before it was ready, so nothing said is lost.
    bool CopyDictatedText(string text);

    string? ActiveLanguage();

    IReadOnlyList<string> InstalledLanguages();

    string DictationAppName();

    void ReportDictation(string message);
}
