using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Input;

namespace Lapsus.Tests;

internal readonly record struct FakeFocus(int Container, int Element) : ITypingFocus
{
    public bool IsEmpty => Container == 0;
}

internal sealed class FakeInputBackend(IPhraseCorrector corrector, AppExclusions? excludedApps = null)
    : InputBackendBase<FakeFocus>(corrector, excludedApps)
{
    public static readonly InstalledLayout En = new(
        "en-US", 1, Script.Latin, "en", KeyboardLayout.En, BundledKeyboardMaps.En);

    public static readonly InstalledLayout Uk = new(
        "uk-UA", 2, Script.Cyrillic, "uk", KeyboardLayout.Uk, BundledKeyboardMaps.Uk);

    public static readonly InstalledLayout Ru = new(
        "ru-RU", 3, Script.Cyrillic, "ru", KeyboardLayout.Ru, BundledKeyboardMaps.Ru);

    public List<InstalledLayout> InstalledLayouts { get; set; } = [En, Uk];

    public string? BlockedReason { get; set; }

    public int BlockedReasonAsked { get; private set; }

    public List<(int Backspaces, string Text)> Injected { get; } = [];

    public List<string> Diagnostics { get; } = [];

    public List<(KeyboardLayout? Target, string? Id)> LayoutSwitches { get; } = [];

    public FakeInputBackend Listening()
    {
        Diagnostic += (_, message) => Diagnostics.Add(message);
        return this;
    }

    public void PressHotkey()
    {
        ApplyCorrection();
    }

    public void DoubleTap(TapModifier modifier)
    {
        NoteModifierKey(modifier, true);
        NoteModifierKey(modifier, false);
        NoteModifierKey(modifier, true);
        NoteModifierKey(modifier, false);
    }

    public FakeInputBackend Focused()
    {
        ApplyFocusSnapshot(new FakeFocus(1, 1), false);
        return this;
    }

    public void Type(string text)
    {
        TypeOn(1, text);
    }

    public void TypeOn(long layoutToken, string text)
    {
        // As CaptureKey on both platforms: a keystroke ends the hotkey circle before it is buffered.
        foreach (var ch in text)
        {
            EndCorrectionCycle();
            NoteKeystrokeLayout(layoutToken);
            AppendCapturedChar(ch);
        }
    }

    public void DiscardLine()
    {
        ResetTypingBuffer();
    }

    public void Backspace(bool withModifier = false)
    {
        EndCorrectionCycle();
        BackspaceTypingBuffer(withModifier);
    }

    public override bool IsRunning => true;

    public override bool SkipPasswordFields { get; set; }

    public override void Start()
    {
    }

    public override void Stop()
    {
    }

    protected override List<InstalledLayout> EnumerateLayouts()
    {
        return InstalledLayouts;
    }

    protected override InstalledLayout? FindActiveLayout(IReadOnlyList<InstalledLayout> layouts)
    {
        return layouts[0];
    }

    protected override bool TryInject(int backspaces, string text)
    {
        Injected.Add((backspaces, text));
        return true;
    }

    protected override void ApplyLayoutSwitch(KeyboardLayout? target, string? layoutId)
    {
        LayoutSwitches.Add((target, layoutId));
    }

    protected override string ForegroundAppName()
    {
        return "TestApp";
    }

    protected override string? ForegroundProcessName()
    {
        return "TestApp";
    }

    protected override string? PlatformInputBlockedReason()
    {
        return BlockedReason;
    }

    protected override string? TypingBlockedReason()
    {
        BlockedReasonAsked++;
        return base.TypingBlockedReason();
    }

    protected override Task<string?> CopySelectionAsync()
    {
        return Task.FromResult<string?>(null);
    }

    protected override bool PasteText(string text)
    {
        return true;
    }

    protected override Task RestoreClipboardAsync()
    {
        return Task.CompletedTask;
    }

    protected override bool IsSameContainer(FakeFocus owner, FakeFocus focus)
    {
        return owner.Container == focus.Container;
    }

    protected override bool IsStillFocused(FakeFocus ownerAtStart)
    {
        return !ownerAtStart.IsEmpty;
    }
}

internal sealed class TableCorrector(params (string From, string To)[] table) : IPhraseCorrector
{
    public List<string> Asked { get; } = [];

    public bool IsReady => true;

    public bool SupportsLayoutCycle => true;

    public bool PreferAsync => false;

    public bool SupportsAutoMode => true;

    public PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred = null)
    {
        Asked.Add(text);
        foreach (var (from, to) in table)
            if (text == from)
                return new PhraseCorrection(text, to, true, KeyboardLayout.Uk, "uk-UA");

        return new PhraseCorrection(text, text, false, null);
    }
}

internal sealed class GatedAsyncCorrector(string from, string to) : IPhraseCorrector, IDisposable
{
    public ManualResetEventSlim Entered { get; } = new();

    public ManualResetEventSlim Release { get; } = new();

    public int Calls { get; private set; }

    public bool IsReady => true;

    public bool SupportsLayoutCycle => false;

    public bool PreferAsync => true;

    public PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred = null)
    {
        Calls++;
        Entered.Set();
        Release.Wait(TimeSpan.FromSeconds(5));
        return text == from
            ? new PhraseCorrection(text, to, true, KeyboardLayout.Uk, "uk-UA")
            : new PhraseCorrection(text, text, false, null);
    }

    public void Dispose()
    {
        Entered.Dispose();
        Release.Dispose();
    }
}

// Holds each request until it is cancelled, keeping every token it was handed.
internal sealed class CancellableAsyncCorrector : IPhraseCorrector, IDisposable
{
    public SemaphoreSlim Entered { get; } = new(0);

    public List<CancellationToken> Tokens { get; } = [];

    public bool IsReady => true;

    public bool SupportsLayoutCycle => false;

    public bool PreferAsync => true;

    public PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred = null)
    {
        return CorrectPhrase(text, active, installed, candidates, preferred, default);
    }

    public PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred,
        CorrectionHints hints)
    {
        lock (Tokens)
            Tokens.Add(hints.Cancellation);
        Entered.Release();
        hints.Cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
        hints.Cancellation.ThrowIfCancellationRequested();
        return new PhraseCorrection(text, text, false, null);
    }

    public void Dispose()
    {
        Entered.Dispose();
    }
}

internal sealed class ScriptedCorrector(string from, string to) : IPhraseCorrector
{
    public string To { get; set; } = to;

    public int Calls { get; private set; }

    public bool IsReady => true;

    public bool SupportsLayoutCycle => false;

    public bool PreferAsync => false;

    public bool SupportsAutoMode => true;

    public PhraseCorrection CorrectPhrase(
        string text,
        LayoutSource active,
        IReadOnlyList<LayoutSource> installed,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred = null)
    {
        Calls++;
        return text == from
            ? new PhraseCorrection(text, To, true, KeyboardLayout.Uk, "uk-UA")
            : new PhraseCorrection(text, text, false, null);
    }
}
