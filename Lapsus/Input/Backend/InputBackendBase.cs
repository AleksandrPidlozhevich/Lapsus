using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Core.Text;
using Lapsus.Localization;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input;

internal interface ITypingFocus
{
    bool IsEmpty { get; }
}

internal abstract class InputBackendBase<TFocus> : IInputBackend
    where TFocus : struct, ITypingFocus, IEquatable<TFocus>
{

    private static readonly TimeSpan IdleWindow = TimeSpan.FromMinutes(1);

    private readonly TypingBuffer _buffer = new();

    private readonly ModifierTapDetector _tapDetector = new();

    private int _asyncCorrectionGeneration;

    private int _bufferGeneration;

    private int _neuralRewriteInFlight;

    // The request a model is working on; the next one, or a brain swap, cancels it so the model is free.
    private CancellationTokenSource? _modelRequest;

    private bool _selectionActionRunning;

    protected bool SelectionActionInProgress => _selectionActionRunning;

    protected List<InstalledLayout>? Layouts;

    private LayoutCorrectionCycle? _cycle;

    private int _committedLength;

    // Words ended by a space whose auto-correction is queued on the UI thread, oldest first.
    private readonly List<PendingAutoFix> _pendingAutoFixes = [];

    protected TFocus BufferOwner;

    private Script? _lineDirection;

    protected InputBackendBase(IPhraseCorrector corrector, AppExclusions? excludedApps = null)
    {
        Corrector = corrector;

        ExcludedApps = excludedApps ?? new AppExclusions();
    }

    protected AppExclusions ExcludedApps { get; }

    protected IPhraseCorrector Corrector { get; private set; }

    public abstract bool IsRunning { get; }

    public int HotkeyVirtualKey { get; set; }

    private readonly int[] _selectionTriggers = new int[Enum.GetValues<SelectionAction>().Length];

    public void SetSelectionHotkey(SelectionAction action, int trigger)
    {
        _selectionTriggers[(int)action] = trigger;
    }

    protected SelectionAction? SelectionActionFor(int trigger)
    {
        if (trigger == 0)
            return null;

        for (var i = 0; i < _selectionTriggers.Length; i++)
            if (_selectionTriggers[i] == trigger)
                return (SelectionAction)i;

        return null;
    }

    public bool AutoMode { get; set; }

    public bool AutoFixTypos { get; set; }

    public bool SwitchSystemLayout { get; set; }

    public KeyboardLayout? PreferredLayout { get; set; }

    public abstract bool SkipPasswordFields { get; set; }

    public event EventHandler<CorrectionResult>? Corrected;

    public event EventHandler<string>? Diagnostic;

    public abstract void Start();

    public abstract void Stop();

    public virtual void Dispose()
    {
        Stop();
    }

    public void SetCorrector(IPhraseCorrector corrector)
    {
        Corrector = corrector;
        _cycle = null;
        _asyncCorrectionGeneration++;
        _modelRequest?.Cancel();
    }

    private CancellationTokenSource BeginModelRequest()
    {
        _modelRequest?.Cancel();
        _modelRequest = new CancellationTokenSource();
        return _modelRequest;
    }

    private void EndModelRequest(CancellationTokenSource request)
    {
        if (ReferenceEquals(_modelRequest, request))
            _modelRequest = null;
        request.Dispose();
    }

    public IReadOnlyList<LayoutCandidate> LayoutCandidates()
    {
        return InstalledLayoutCorrection.BuildCandidates(ReadLayouts().All);
    }

    protected abstract List<InstalledLayout> EnumerateLayouts();

    protected abstract InstalledLayout? FindActiveLayout(IReadOnlyList<InstalledLayout> layouts);

    protected abstract bool TryInject(int backspaces, string text);

    protected abstract void ApplyLayoutSwitch(KeyboardLayout? target, string? layoutId);

    protected abstract string ForegroundAppName();

    protected string? InputBlockedReason()
    {
        if (IsExcludedApp(out var process))
            return Localizer.Instance.Format("Diag_AppExcluded", process);

        return PlatformInputBlockedReason();
    }

    protected abstract string? PlatformInputBlockedReason();

    protected abstract string? ForegroundProcessName();

    protected bool IsExcludedApp(out string? process)
    {
        process = ForegroundProcessName();
        return ExcludedApps.Contains(process);
    }

    protected virtual string? TypingBlockedReason()
    {
        return InputBlockedReason();
    }

    protected abstract Task<string?> CopySelectionAsync();

    protected abstract bool PasteText(string text);

    protected abstract Task RestoreClipboardAsync();

    protected abstract bool IsSameContainer(TFocus owner, TFocus focus);

    protected abstract bool IsStillFocused(TFocus ownerAtStart);

    protected void RaiseDiagnostic(string message)
    {
        Diagnostic?.Invoke(this, message);
    }

    protected void RaiseDiagnosticFormat(string key, params object[] args)
    {
        Diagnostic?.Invoke(this, Localizer.Instance.Format(key, args));
    }

    protected void NoteModifierKey(TapModifier modifier, bool isDown)
    {
        if (_tapDetector.Note(modifier, isDown, Environment.TickCount64) is not { } tapped)
            return;

        var trigger = HotkeyTriggers.DoubleTap(tapped);
        if (trigger == HotkeyVirtualKey)
            Dispatcher.UIThread.Post(ApplyCorrection);
        else if (SelectionActionFor(trigger) is { } action)
            Dispatcher.UIThread.Post(() => _ = ApplySelectionActionAsync(action));
    }

    protected void NoteNonModifierInput()
    {
        _tapDetector.NoteOtherInput();
    }

    protected void ResetTypingBuffer()
    {
        _bufferGeneration++;
        _buffer.Reset();
        _pendingAutoFixes.Clear();
        _cycle = null;
        _committedLength = 0;
        _lineDirection = null;
        _atWordStart = true;
        _layoutChangedMidWord = false;
    }

    protected void EndCorrectionCycle()
    {
        if (_cycle is null)
            return;

        _cycle = null;
        _committedLength = _buffer.Length;
    }

    protected void DropTypingBufferIfIdle()
    {
        if (_buffer.DropIfIdle(IdleWindow))
            ResetTypingBuffer();
    }

    protected void BackspaceTypingBuffer(bool withModifier = false)
    {
        if (withModifier)
        {
            ResetTypingBuffer();
            return;
        }

        _bufferGeneration++;
        _buffer.Backspace();
        TrimPendingAutoFixes();
        _committedLength = Math.Min(_committedLength, _buffer.Length);

        _atWordStart = !_buffer.EndsInsideChunk;
        if (_atWordStart)
            _layoutChangedMidWord = false;
    }

    protected void ClearTypingContext(TFocus newOwner)
    {
        ResetTypingBuffer();
        BufferOwner = newOwner;
    }

    protected bool ApplyFocusSnapshot(TFocus focus, bool isTransient)
    {
        if (focus.IsEmpty)
        {
            if (_neuralRewriteInFlight > 0)
                return true;

            if (_buffer.Length > 0 || _cycle is not null || !BufferOwner.IsEmpty)
                ClearTypingContext(default);
            return true;
        }

        if (isTransient)
            return false;

        if (BufferOwner.IsEmpty)
        {
            BufferOwner = focus;
            return false;
        }

        if (BufferOwner.Equals(focus))
            return false;

        if (IsSameContainer(BufferOwner, focus))
        {
            BufferOwner = focus;
            return false;
        }

        if (_neuralRewriteInFlight > 0)

            return true;

        ClearTypingContext(focus);
        return true;
    }

    protected void AppendCapturedText(ReadOnlySpan<char> text)
    {
        foreach (var ch in text)
            AppendCapturedChar(ch);
    }

    protected void NoteKeystrokeLayout(long layoutToken)
    {
        if (_atWordStart)
        {
            _wordLayoutToken = layoutToken;
            _layoutChangedMidWord = false;
            return;
        }

        if (layoutToken != _wordLayoutToken)
            _layoutChangedMidWord = true;
    }

    private long _wordLayoutToken;
    private bool _layoutChangedMidWord;
    private bool _atWordStart = true;

    protected void AppendCapturedChar(char ch)
    {
        // Enter must ResetTypingBuffer (not buffer alone), or leftover _committedLength blinds the next line.
        if (char.IsControl(ch))
        {
            ResetTypingBuffer();
            return;
        }

        // Keystrokes typed after a queued word's space are its tail; its correction rewrites the word and the tail.
        foreach (var pending in _pendingAutoFixes)
            pending.Tail += ch;

        if ((AutoMode || AutoFixTypos) && Corrector.SupportsAutoMode && ch == ' ' && !_layoutChangedMidWord)
        {
            var completed = _buffer.CurrentChunk;
            if (completed.Length > 0 && completed.Length <= _buffer.Length - _committedLength)
            {
                var pending = new PendingAutoFix(completed, BufferOwner);
                _pendingAutoFixes.Add(pending);
                Dispatcher.UIThread.Post(() => AutoCorrect(pending));
            }
        }

        _bufferGeneration++;
        _buffer.Append(ch);

        _atWordStart = char.IsWhiteSpace(ch);
        if (_atWordStart)
            _layoutChangedMidWord = false;
    }

    // A backspace removes the last character: from a tail it shortens the tail; from a queued word's space it
    // removes that word, since its correction no longer has the space it was queued for.
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

        // The line must still read "word<space>tail"; a hotkey or a neural rewrite since the space changes it.
        if (!Corrector.IsReady || !_buffer.Segment.EndsWith(word + " " + tail, StringComparison.Ordinal))
            return;

        if (!AutoCorrectPolicy.IsEligible(word, _lineDirection is not null))
            return;

        // Typos only asks the brain for spelling inside the typed layout. A brain that ignores the hint can still
        // answer with another layout, so the result is checked below as well.
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

        // Typos only keeps a word as typed when the answer still names another layout.
        if (!AutoMode && (phrase.TargetLayout is not null || !string.IsNullOrEmpty(phrase.TargetLayoutId)))
            return;

        NoteLineDirection(word, phrase);
        if (!phrase.Changed)
            return;

        // The tail was typed into the field already; it is retyped after the corrected word so the caret ends where it was.
        var replacement = phrase.Corrected + " " + tail;
        if (!TryInject(word.Length + 1 + tail.Length, replacement))
            return;

        _bufferGeneration++;
        if (_buffer.TryReplaceTrailing(word + " " + tail, replacement))
        {
            _committedLength = _buffer.Length - replacement.Length;

            // A cycle walks the corrected word alone, which is only the whole segment while nothing followed it.
            if (tail.Length == 0 && source is not null && Layouts is not null)
                _cycle = LayoutCorrectionCycle.StartAuto(word, phrase, source, Layouts, Corrector.SupportsLayoutCycle);
        }
        else
        {
            ResetTypingBuffer();
        }

        RaiseCorrected(word, phrase.Corrected, source?.Target, phrase.TargetLayout);
        SwitchLayoutIfRequested(phrase.TargetLayout, phrase.TargetLayoutId);
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

    protected void ApplyCorrection()
    {
        if (TypingBlockedReason() is { } blocked)
        {
            RaiseDiagnostic(blocked);
            return;
        }

        _lineDirection = null;

        DropTypingBufferIfIdle();

        var segment = _buffer.Segment[Math.Min(_committedLength, _buffer.Length)..];
        if (string.IsNullOrWhiteSpace(segment))
        {
            RaiseDiagnosticFormat("Diag_Empty", ForegroundAppName());
            return;
        }

        if (!Corrector.IsReady)
        {
            RaiseDiagnostic(Localizer.Instance[Corrector.PreferAsync ? "Diag_NoModel" : "Diag_NoDictionaries"]);
            return;
        }

        if (_cycle is not null && _cycle.MatchesCurrentText(segment))
        {
            var next = _cycle.MoveNext();
            ApplyHotkeyStep(segment, next.Text, next.TargetLayout, next.TargetLayoutId, _cycle.OriginalLayout);
            return;
        }

        if (Corrector.PreferAsync)
        {
            PendingNeuralRewrite = ApplyNeuralCorrectionAsync(segment);
            return;
        }

        LayoutCorrectionCycle? cycle;
        try
        {
            cycle = StartCorrectionCycle(segment);
        }
        catch (Exception ex)
        {
            // Sync brains run in the hook callback; an exception escaping kills process keyboard input.
            RaiseDiagnosticFormat("Diag_CorrectionFailed", ex.Message);
            return;
        }

        if (cycle is null)
        {
            RaiseDiagnosticFormat("Diag_NoChange", segment);
            return;
        }

        _cycle = cycle;
        var current = cycle.Current;
        ApplyHotkeyStep(segment, current.Text, current.TargetLayout, current.TargetLayoutId, cycle.OriginalLayout);
    }

    protected async Task ApplySelectionActionAsync(SelectionAction action)
    {

        if (_selectionActionRunning)
            return;

        if (InputBlockedReason() is { } blocked)
        {
            RaiseDiagnostic(blocked);
            return;
        }

        if (action == SelectionAction.Correct && !Corrector.IsReady)
        {
            RaiseDiagnostic(Localizer.Instance[Corrector.PreferAsync ? "Diag_NoModel" : "Diag_NoDictionaries"]);
            return;
        }

        _selectionActionRunning = true;
        try
        {
            string? selection;
            try
            {
                selection = await CopySelectionAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                RaiseDiagnosticFormat("Diag_ClipboardFailed", ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(selection))
            {
                RaiseDiagnosticFormat("Diag_NoSelection", ForegroundAppName());
                return;
            }

            try
            {
                if (action != SelectionAction.Correct)
                {
                    ApplySelectionTransform(action, selection);
                    return;
                }

                var layouts = ReadLayouts();
                var generation = _asyncCorrectionGeneration;
                var request = BeginModelRequest();
                var hints = new CorrectionHints(Cancellation: request.Token);
                (PhraseCorrection Phrase, InstalledLayout? Source) result;
                try
                {
                    result = await Task.Run(
                        () => (Phrase: CorrectWith(selection, layouts.Active, layouts.All, hints),
                            Source: layouts.Active)).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    EndModelRequest(request);
                }

                if (generation != _asyncCorrectionGeneration)
                    return;

                if (!result.Phrase.Changed)
                {
                    RaiseDiagnosticFormat("Diag_NoChange", Shorten(selection));
                    return;
                }

                if (!PasteText(result.Phrase.Corrected))
                    return;

                RaiseCorrected(Shorten(selection), Shorten(result.Phrase.Corrected),
                    result.Source?.Target, result.Phrase.TargetLayout);
                SwitchLayoutIfRequested(result.Phrase.TargetLayout, result.Phrase.TargetLayoutId);
            }
            catch (Exception ex)
            {
                RaiseDiagnosticFormat(Corrector.PreferAsync ? "Diag_NeuralFailed" : "Diag_CorrectionFailed", ex.Message);
            }
        }
        finally
        {

            try
            {
                await RestoreClipboardAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                RaiseDiagnosticFormat("Diag_ClipboardFailed", ex.Message);
            }
            finally
            {
                _selectionActionRunning = false;
            }
        }
    }

    private void ApplySelectionTransform(SelectionAction action, string selection)
    {

        var (latinTarget, language) = action == SelectionAction.Transliterate
            ? LatinTransliterationTarget()
            : (Script.Cyrillic, null);

        var transformed = SelectionTransforms.Apply(action, selection, latinTarget, language);
        if (string.Equals(transformed, selection, StringComparison.Ordinal))
        {
            RaiseDiagnosticFormat("Diag_NoChange", Shorten(selection));
            return;
        }

        if (!PasteText(transformed))
            return;

        RaiseCorrected(Shorten(selection), Shorten(transformed), null, null);
    }

    private (Script Script, string? Language) LatinTransliterationTarget()
    {

        switch (PreferredLayout)
        {
            case KeyboardLayout.El: return (Script.Greek, "el");
            case KeyboardLayout.He: return (Script.Hebrew, "he");
            case KeyboardLayout.Ar: return (Script.Arabic, "ar");
        }

        var preferredLanguage = PreferredLayout is { } preferred ? LayoutLanguage.FromKeyboardLayout(preferred) : null;

        Script? other = null;
        foreach (var layout in ReadLayouts().All)
        {
            if (layout.Script == Script.Cyrillic)
                return (Script.Cyrillic, preferredLanguage ?? layout.LanguageCode);

            if (layout.Script is Script.Greek or Script.Hebrew or Script.Arabic)
                other ??= layout.Script;
        }

        return other switch
        {
            Script.Greek => (Script.Greek, "el"),
            Script.Hebrew => (Script.Hebrew, "he"),
            Script.Arabic => (Script.Arabic, "ar"),
            _ => (Script.Cyrillic, preferredLanguage)
        };
    }

    private static string Shorten(string text)
    {
        const int limit = 60;
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= limit ? flat : flat[..limit] + "…";
    }

    internal Task? PendingNeuralRewrite { get; private set; }

    private async Task ApplyNeuralCorrectionAsync(string segment)
    {
        var generation = ++_asyncCorrectionGeneration;
        var bufferAtStart = _bufferGeneration;
        var ownerAtStart = BufferOwner;
        var request = BeginModelRequest();
        var hints = new CorrectionHints(Cancellation: request.Token);
        _neuralRewriteInFlight++;
        (PhraseCorrection Phrase, InstalledLayout? Source) result;
        try
        {
            // Snapshot layouts before hopping off the UI thread; Layouts is UI-thread state.
            var layouts = ReadLayouts();
            result = await Task.Run(
                () => (CorrectWith(segment, layouts.Active, layouts.All, hints), layouts.Active))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // A newer request replaced this one; it reports for both.
            return;
        }
        catch (Exception ex)
        {
            RaiseDiagnosticFormat("Diag_NeuralFailed", ex.Message);
            return;
        }
        finally
        {
            _neuralRewriteInFlight--;
            EndModelRequest(request);
        }

        if (generation != _asyncCorrectionGeneration)
            return;

        if (!IsStillFocused(ownerAtStart))
        {
            RaiseDiagnosticFormat("Diag_FocusChanged", ForegroundAppName());
            return;
        }

        if (bufferAtStart != _bufferGeneration)
        {
            RaiseDiagnosticFormat("Diag_BufferChanged", segment);
            return;
        }

        var phrase = result.Phrase;
        if (!phrase.Changed)
        {
            RaiseDiagnosticFormat("Diag_NoChange", segment);
            return;
        }

        _cycle = null;
        if (!ApplyRenderedText(segment, phrase.Corrected))
            return;

        if (result.Source is { } source)
            _cycle = LayoutCorrectionCycle.StartFromAnswer(
                segment, phrase, source, ReadLayouts().All, Corrector.SupportsLayoutCycle);

        RaiseCorrected(segment, phrase.Corrected, result.Source?.Target, phrase.TargetLayout);
        SwitchLayoutOnHotkey(phrase.TargetLayout, phrase.TargetLayoutId);
    }

    private void SwitchLayoutIfRequested(KeyboardLayout? target, string? layoutId)
    {
        if (SwitchSystemLayout && (target is not null || !string.IsNullOrEmpty(layoutId)))
            ApplyLayoutSwitch(target, layoutId);
    }

    private void SwitchLayoutOnHotkey(KeyboardLayout? target, string? layoutId)
    {
        if (target is not null || !string.IsNullOrEmpty(layoutId))
            ApplyLayoutSwitch(target, layoutId);
    }

    private bool ApplyHotkeyStep(
        string currentText, string rendered, KeyboardLayout? target, string? layoutId, KeyboardLayout? from)
    {

        SwitchLayoutOnHotkey(target, layoutId);

        if (!string.Equals(currentText, rendered, StringComparison.Ordinal)
            && !ApplyRenderedText(currentText, rendered))
            return false;

        RaiseCorrected(currentText, rendered, from, target);
        return true;
    }

    private void RaiseCorrected(string original, string corrected, KeyboardLayout? from, KeyboardLayout? to)
    {

        Corrected?.Invoke(this, new CorrectionResult(original, corrected, 1.0, true, from, to ?? from));
    }

    private bool ApplyRenderedText(string currentText, string rendered)
    {
        if (!TryInject(currentText.Length, rendered))
            return false;

        _bufferGeneration++;
        if (!_buffer.TryReplaceTrailing(currentText, rendered))
            ResetTypingBuffer();

        return true;
    }

    private (InstalledLayout? Active, IReadOnlyList<InstalledLayout> All) ReadLayouts()
    {
        var active = ActiveInstalledLayout();
        return (active, Layouts ?? []);
    }

    private PhraseCorrection CorrectWith(
        string text, InstalledLayout? active, IReadOnlyList<InstalledLayout> all, CorrectionHints hints = default)
    {
        return active is null
            ? new PhraseCorrection(text, text, false, null)
            : InstalledLayoutCorrection.Correct(Corrector, text, active, all, PreferredLayout, hints);
    }

    private PhraseCorrection TryCorrectInstalled(string text, CorrectionHints hints, out InstalledLayout? source)
    {
        var (active, all) = ReadLayouts();
        source = active;
        return CorrectWith(text, active, all, hints);
    }

    private LayoutCorrectionCycle? StartCorrectionCycle(string segment)
    {
        var source = ActiveInstalledLayout();
        return source is null
            ? null
            : LayoutCorrectionCycle.Start(Corrector, segment, source, Layouts!, PreferredLayout);
    }

    private InstalledLayout? ActiveInstalledLayout()
    {
        Layouts ??= EnumerateLayouts();
        if (Layouts.Count == 0)
            return null;

        if (FindActiveLayout(Layouts) is { } known)
            return known;

        Layouts = EnumerateLayouts();
        return Layouts.Count == 0 ? null : FindActiveLayout(Layouts);
    }
}
