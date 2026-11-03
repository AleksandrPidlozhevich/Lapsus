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

internal abstract partial class InputBackendBase<TFocus>
{
    // A backspace into a queued word's space removes that word: its correction no longer has the space it was queued for.
    private void TrimPendingAutoFixes()
    {
        for (var i = _pendingAutoFixes.Count - 1; i >= 0; i--)
        {
            var pending = _pendingAutoFixes[i];
            if (pending.Tail.Length > 0)
                pending.Tail = pending.Tail[..^1];
            else
                _pendingAutoFixes.RemoveAt(i);
        }
    }

    private void AutoCorrect(PendingAutoFix fix)
    {
        // Gone when a reset, a backspace into it, or a focus change ran after its space.
        if (!_pendingAutoFixes.Remove(fix))
            return;

        var word = fix.Word;
        var tail = fix.Tail;

        if (!IsStillFocused(fix.Owner))
            return;

        // The hook keeps letters typed while a field's password check is still pending; nothing may be
        // injected into that field until the check has cleared it.
        if (InputBlockedReason() is not null)
            return;

        // The line must still read "word<space>tail"; a hotkey or a neural rewrite since the space changes it.
        if (!Corrector.IsReady || !_buffer.Segment.EndsWith(word + " " + tail, StringComparison.Ordinal))
            return;

        if (!AutoCorrectPolicy.IsEligible(word, _lineDirection is not null))
        {
            if (AutoMode && _lineDirection is null && AutoCorrectPolicy.IsEligible(word, true))
                _skippedRun.Add(word);
            else
                _skippedRun.Clear();
            return;
        }

        // The run is read again only if this word settles the direction, and only while the line still holds it.
        var run = _skippedRun.Count > 0 && AutoMode && _lineDirection is null &&
                  _buffer.Segment.EndsWith(string.Concat(_skippedRun.Select(w => w + " ")) + word + " " + tail,
                      StringComparison.Ordinal)
            ? _skippedRun.ToArray()
            : [];
        _skippedRun.Clear();

        // A brain that ignores the typo-only hint can still name another layout, so the result is checked below.
        var hints = AutoMode
            ? new CorrectionHints(_lineDirection, KeysOnly: !AutoFixTypos)
            : new CorrectionHints(TypoOnly: true);

        PhraseCorrection phrase;
        InstalledLayout? source;
        try
        {
            phrase = TryCorrectInstalled(word, hints, out source);
        }
        catch (Exception ex)
        {
            RaiseDiagnosticFormat("Diag_CorrectionFailed", ex.Message);
            return;
        }

        if (!AutoMode && (phrase.TargetLayout is not null || !string.IsNullOrEmpty(phrase.TargetLayoutId)))
            return;

        var crossedInto = _lineDirection is null && phrase.Changed &&
                          Scripts.Dominant(phrase.Corrected) is { } into && into != Scripts.Dominant(word)
            ? into
            : (Script?)null;
        NoteLineDirection(word, phrase);
        if (!phrase.Changed)
            return;

        var lead = crossedInto is { } direction ? ReadRunAgain(run, direction) : string.Empty;
        var original = string.Concat(run.Select(w => w + " ")) + word + " " + tail;
        if (lead.Length == 0)
            original = word + " " + tail;

        // The tail was typed into the field already; it is retyped after the corrected word so the caret ends where it was.
        var replacement = lead + phrase.Corrected + " " + tail;
        if (!TryInject(original.Length, replacement))
            return;

        _bufferGeneration++;
        if (_buffer.TryReplaceTrailing(original, replacement))
        {
            _committedLength = _buffer.Length - replacement.Length;

            // A cycle walks the corrected word alone, which is only the whole segment while nothing followed it.
            if (tail.Length == 0 && source is not null && Layouts is not null)
                _cycle = LayoutCorrectionCycle.StartAuto(word, phrase, source, Layouts, Corrector);
        }
        else
        {
            ResetTypingBuffer();
        }

        RaiseCorrected(word, phrase.Corrected, source?.Target, phrase.TargetLayout);
        SwitchLayoutIfRequested(phrase.TargetLayout, phrase.TargetLayoutId);
    }

    private string ReadRunAgain(string[] run, Script direction)
    {
        var read = new StringBuilder();
        var changed = false;
        foreach (var skipped in run)
        {
            var again = skipped;
            try
            {
                var phrase = TryCorrectInstalled(skipped, new CorrectionHints(direction, KeysOnly: !AutoFixTypos), out _);
                if (phrase.Changed && Scripts.Dominant(phrase.Corrected) == direction)
                {
                    again = phrase.Corrected;
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                RaiseDiagnosticFormat("Diag_CorrectionFailed", ex.Message);
            }

            read.Append(again).Append(' ');
        }

        return changed ? read.ToString() : string.Empty;
    }

    private sealed class PendingAutoFix(string word, TFocus owner)
    {
        public string Word { get; } = word;

        public TFocus Owner { get; } = owner;

        // What was typed after the word's space while its correction waited for the UI thread.
        public string Tail { get; set; } = string.Empty;
    }

    private void NoteLineDirection(string word, in PhraseCorrection phrase)
    {
        var typed = Scripts.Dominant(word);
        if (phrase.Changed)
        {
            if (Scripts.Dominant(phrase.Corrected) is { } into && into != typed)
                _lineDirection = into;
            return;
        }

        if (phrase.Settled is { Count: > 0 } settled && TextSpans.CoversEveryLetter(word, settled))
            _lineDirection = null;
    }
}
