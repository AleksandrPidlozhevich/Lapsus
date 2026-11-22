using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Core.Speech;
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

internal abstract partial class InputBackendBase<TFocus>
{

    public int DictationHotkey { get; set; }

    // Read on the key hook's thread: Esc cancels the dictation while this is true.
    public bool DictationActive { get; set; }

    public event Action? DictationHotkeyPressed;

    public event Action<bool>? DictationHotkeyReleased;

    public event Action? DictationCancelPressed;

    public event Action<string>? GlossaryTermSelected;

    // Longer than this is a sentence, not a term.
    private const int MaxGlossaryTerm = 60;

    private void AddSelectionToGlossary(string selection)
    {
        var term = selection.Trim();
        if (term.Length > MaxGlossaryTerm || term.Contains('\n'))
        {
            RaiseDiagnostic(Localizer.Instance["Diag_GlossaryTooLong"]);
            return;
        }

        GlossaryTermSelected?.Invoke(term);
        RaiseDiagnosticFormat("Diag_GlossaryAdded", term);
    }

    protected void RaiseDictationHotkey() => DictationHotkeyPressed?.Invoke();

    protected void RaiseDictationReleased(bool interrupted = false) => DictationHotkeyReleased?.Invoke(interrupted);

    protected void RaiseDictationCancel() => DictationCancelPressed?.Invoke();

    // UI thread. True when this completed the chord.
    protected bool NoteDictationModifiers(HotkeyModifiers held) => _dictationChord.NoteModifiers(held);

    // UI thread.
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

    // What was typed into this field before the caret, since the last click or caret move; "" when unknown.
    public string DictationTextBefore() => _buffer.Segment;

    public bool InsertDictatedText(string text)
    {
        if (text.Length == 0)
            return false;

        var line = _buffer.Segment;
        var spaced = DictationText.FitAfter(line, text);
        if (!TryInject(0, spaced))
            return false;
        // Parts typed one after another are one dictation to learn from.
        var dictatedFrom = _dictated is { } earlier && earlier.Line == line ? earlier.From : line.Length;

        // Parts typed one after another with nothing in between are taken back together.
        var undo = _dictationUndo is { } last && last.Generation == _bufferGeneration && !last.Removed
            ? last.Text + spaced
            : spaced;

        // The dictated text joins the line, committed, so the next dictation knows what precedes the caret
        // and the correction hotkey does not take it for something typed on the wrong layout.
        ResetTypingBuffer();
        foreach (var ch in line + spaced)
            _buffer.Append(ch);
        _committedLength = _buffer.Length;
        _dictationUndo = (undo, false, _bufferGeneration);
        _dictated = (line + spaced, dictatedFrom);
        return true;
    }

    // The line as the last dictation left it and where the dictated text starts in it, until the caret moves away: a
    // word the user then backspaces over and retypes in Latin is a term to learn (GlossaryLearner).
    private (string Line, int From)? _dictated;

    // At the end of each word typed after a dictation. The keys typed since are in the buffer, so its line is the
    // field's as long as the caret stayed put.
    private void LearnFromCorrection()
    {
        if (_dictated is not { } dictated)
            return;

        var now = _buffer.Segment;
        if (now.Length < dictated.From || !now.AsSpan(0, dictated.From).SequenceEqual(dictated.Line.AsSpan(0, dictated.From)))
        {
            _dictated = null;
            return;
        }

        var learned = GlossaryLearner.Learn(dictated.Line[dictated.From..], now[dictated.From..]);
        if (learned.Count == 0)
            return;

        // The corrected line is what later fixes are measured against.
        _dictated = (now, dictated.From);
        foreach (var (term, heard) in learned)
            Dispatcher.UIThread.Post(() =>
            {
                GlossaryTermSelected?.Invoke($"{term}|{heard}");
                RaiseDiagnosticFormat("Diag_GlossaryAdded", term);
            });
    }

    // The correction hotkey right after a dictation, before any key, takes it back; pressed again, retypes it.
    private (string Text, bool Removed, int Generation)? _dictationUndo;

    private bool UndoDictation()
    {
        if (_dictationUndo is not { } last || last.Generation != _bufferGeneration)
        {
            _dictationUndo = null;
            return false;
        }

        if (!last.Removed)
        {
            var line = _buffer.Segment;
            if (!line.EndsWith(last.Text, StringComparison.Ordinal) || !TryInject(last.Text.Length, ""))
                return false;
            ResetTypingBuffer();
            foreach (var ch in line[..^last.Text.Length])
                _buffer.Append(ch);
        }
        else
        {
            if (!TryInject(0, last.Text))
                return false;
            foreach (var ch in last.Text)
                _buffer.Append(ch);
        }

        _committedLength = _buffer.Length;
        _dictationUndo = (last.Text, !last.Removed, _bufferGeneration);
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
