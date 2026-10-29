using Avalonia.Threading;
using Lapsus.Audio;
using Lapsus.Localization;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input.Dictation;

// One dictation at a time, driven by a single hotkey: hold it while speaking, or tap it to start and tap again
// to stop; what was said is typed into the field that had focus when it started. Esc cancels. The microphone opens at once and
// the model loads while the user speaks; the text goes nowhere if focus moved to another field meanwhile.
// Runs on the UI thread.
internal sealed class DictationSession : IDisposable
{
    public static readonly TimeSpan MaxRecording = TimeSpan.FromMinutes(5);

    // A recording left running in silence this long stops by itself.
    public static readonly TimeSpan MaxSilence = TimeSpan.FromSeconds(60);

    // A dictation into the same field soon after the last one continues it (the prompt gets the earlier text).
    private static readonly TimeSpan ContinueWithin = TimeSpan.FromMinutes(2);

    // RMS above this is someone speaking; never above DeadQuiet means the microphone gave pure zeros
    // (switched off, or Windows privacy settings keep apps from it).
    private const float SpeechLevel = 0.01f;
    private const float DeadQuiet = 0.0003f;

    private const int SampleRate = 16000;

    // A long dictation is typed part by part while it goes on: once this much has been said since the last
    // part, the next pause this long ends a part, which is recognised and typed while the user keeps talking.
    public static readonly TimeSpan PartAfter = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan PauseEndsPart = TimeSpan.FromMilliseconds(700);

    public enum State { Idle, Listening, Transcribing }

    private readonly IDictationHost _host;
    private readonly IAudioCapture _audio;
    private readonly IDictationEngine _engine;
    private readonly IDictationIndicator _indicator;
    private readonly Func<bool> _enabled;
    private readonly Func<DateTime> _now;
    private readonly DispatcherTimer? _watchdog;

    private object? _target;
    private string _language = "en";
    private System.Collections.Generic.IReadOnlyList<string> _languages = [];
    private DateTime _startedAt;
    private long _lastLoudTicks;
    private float _peak;
    private (object Target, string Text, DateTime At)? _previous;

    // This dictation: Esc cancels it whole; what was already typed stays.
    private CancellationTokenSource? _dictation;
    private string? _continued;
    private string _said = "";
    private string _untyped = "";
    private Task _parts = Task.CompletedTask;
    private Exception? _partFailed;
    private DateTime _lastCut;

    public DictationSession(
        IDictationHost host, IAudioCapture audio, IDictationEngine engine, IDictationIndicator indicator,
        Func<bool> enabled, Func<DateTime>? now = null, bool watchdog = true)
    {
        _host = host;
        _audio = audio;
        _engine = engine;
        _indicator = indicator;
        _enabled = enabled;
        _now = now ?? (() => DateTime.UtcNow);

        _host.DictationHotkeyPressed += Press;
        _host.DictationHotkeyReleased += Release;
        _host.DictationCancelPressed += Cancel;
        _audio.Level += OnLevel;

        if (watchdog)
        {
            _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _watchdog.Tick += (_, _) => Tick();
            _watchdog.Start();
        }
    }

    public State Current { get; private set; } = State.Idle;

    // The transcription in flight, for tests.
    internal Task? Pending { get; private set; }

    // A press that starts a recording and is held at least this long is hold-to-talk: letting go stops it.
    // A shorter one leaves it running until the next press.
    public static readonly TimeSpan HoldToTalk = TimeSpan.FromMilliseconds(350);

    private DateTime? _startedByPress;
    private bool _stopOnRelease;

    // A click (the on-screen button) or a double tap: press and release at once.
    public void Toggle()
    {
        Press();
        Release(false);
    }

    public void Press()
    {
        switch (Current)
        {
            case State.Idle:
                Start();
                _startedByPress = Current == State.Listening ? _now() : null;
                _stopOnRelease = false;
                break;
            case State.Listening:
                _stopOnRelease = true;
                break;
        }
    }

    public void Release(bool interrupted)
    {
        var startedAt = _startedByPress;
        var stop = _stopOnRelease;
        _startedByPress = null;
        _stopOnRelease = false;
        if (Current != State.Listening)
            return;

        // The keys that started it were part of another shortcut (Ctrl+Win+D): nothing was meant to be said.
        if (interrupted && startedAt is not null)
            Cancel();
        else if (stop || startedAt is { } at && _now() - at >= HoldToTalk)
            Pending = StopAsync();
        else
            TypeWhatIsReady();
    }

    public void Cancel()
    {
        switch (Current)
        {
            case State.Listening:
                _audio.Stop();
                _dictation?.Cancel();
                _host.DictationListening = false;
                Current = State.Idle;
                _indicator.Hide();
                break;
            case State.Transcribing:
                _dictation?.Cancel();
                break;
        }
    }

    internal void Tick()
    {
        if (Current != State.Listening)
            return;

        var now = _now();
        var lastLoud = new DateTime(Interlocked.Read(ref _lastLoudTicks), DateTimeKind.Utc);
        if (now - _startedAt >= MaxRecording || now - lastLoud >= MaxSilence)
            Pending = StopAsync();
        else if (now - _lastCut >= PartAfter && now - lastLoud >= PauseEndsPart && lastLoud > _lastCut)
            CutPart(now);
    }

    // The speech so far goes to recognition while recording goes on; parts are recognised and typed in order.
    private void CutPart(DateTime now)
    {
        _lastCut = now;
        var pcm = _audio.TakeRecorded();
        var before = _parts;
        _parts = RecognisePartAsync(before, pcm, _dictation!.Token);
        Pending = _parts;
    }

    // Never throws: a failure is kept for the end of the dictation, which reports it if nothing was heard.
    private async Task RecognisePartAsync(Task before, float[] pcm, CancellationToken cancel)
    {
        await before.ConfigureAwait(true);
        if (cancel.IsCancellationRequested)
            return;

        try
        {
            Heard(await _engine.TranscribeAsync(pcm, Request(), cancel).ConfigureAwait(true));
            TypeWhatIsReady();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _partFailed = ex;
        }
    }

    // The earlier dictation into this field and everything recognised so far: Whisper's context for the next part.
    private DictationRequest Request()
    {
        var context = string.Join(' ', new[] { _continued, _said }.Where(s => !string.IsNullOrEmpty(s)));
        return new DictationRequest(_language, _languages, context.Length > 0 ? context : null);
    }

    private void Heard(string text)
    {
        if (text.Length == 0)
            return;
        _said = _said.Length > 0 ? _said + " " + text : text;
        _untyped = _untyped.Length > 0 ? _untyped + " " + text : text;
    }

    // Typed now unless the hotkey is held (the letters would arrive with Ctrl down and act as shortcuts) or the
    // field is not in front; then it waits for the release, or for the end of the dictation.
    private void TypeWhatIsReady()
    {
        if (_untyped.Length == 0 || _startedByPress is not null || _stopOnRelease || _target is null)
            return;
        if (!_host.IsDictationTargetFocused(_target) || _host.DictationBlockedReason() is not null)
            return;
        if (_host.InsertDictatedText(_untyped))
            _untyped = "";
    }

    private void Start()
    {
        if (!_enabled())
            return;

        if (_host.DictationBlockedReason() is { } blocked)
        {
            Fail(blocked);
            return;
        }

        if (!_engine.HasModel)
        {
            Fail(Localizer.Instance["Diag_DictationNoModel"]);
            return;
        }

        // macOS records silence until the user allows the microphone, so ask before recording, not after.
        switch (_audio.Access)
        {
            case MicrophoneAccess.Denied:
                Fail(Localizer.Instance["Diag_MicDenied"]);
                _audio.OpenAccessSettings();
                return;
            case MicrophoneAccess.NotAsked:
                _audio.RequestAccess();
                Fail(Localizer.Instance["Diag_MicAsk"]);
                return;
        }

        _target = _host.CaptureDictationTarget();
        _language = _host.ActiveLanguage() ?? "en";
        _languages = _host.InstalledLanguages();

        try
        {
            _audio.Start();
        }
        catch (AudioCaptureException ex)
        {
            Fail(Localizer.Instance.Format("Diag_MicUnavailable", ex.Message));
            return;
        }

        // The microphone is open: the model may now take its time, the speech is being kept.
        _engine.Prepare();

        _startedAt = _now();
        _lastCut = _startedAt;
        _continued = _previous is { } last && Equals(last.Target, _target) && _startedAt - last.At < ContinueWithin
            ? last.Text
            : null;
        _said = "";
        _untyped = "";
        _parts = Task.CompletedTask;
        _partFailed = null;
        _dictation?.Dispose();
        _dictation = new CancellationTokenSource();
        Interlocked.Exchange(ref _lastLoudTicks, _startedAt.Ticks);
        _peak = 0;
        Current = State.Listening;
        _host.DictationListening = true;
        _indicator.ShowListening();
    }

    private async Task StopAsync()
    {
        var pcm = _audio.Stop();
        _host.DictationListening = false;
        Current = State.Transcribing;
        _indicator.ShowWorking();

        if (pcm.Length > SampleRate / 2 && _peak < DeadQuiet)
        {
            Fail(Localizer.Instance["Diag_MicSilent"]);
            return;
        }

        var target = _target!;
        var cancel = _dictation!.Token;
        try
        {
            await _parts.ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (pcm.Length > 0)
                Heard(await _engine.TranscribeAsync(pcm, Request(), cancel).ConfigureAwait(true));
        }
        catch (OperationCanceledException)
        {
            Current = State.Idle;
            _indicator.Hide();
            return;
        }
        catch (Exception ex)
        {
            Fail(Localizer.Instance.Format("Diag_DictationFailed", ex.Message));
            return;
        }

        Current = State.Idle;
        if (_said.Length == 0)
        {
            if (_partFailed is { } failed)
                Fail(Localizer.Instance.Format("Diag_DictationFailed", failed.Message));
            else
                _indicator.ShowMessage(Localizer.Instance["Diag_DictationNothingHeard"]);
            return;
        }

        _previous = (target, string.Join(' ', new[] { _continued, _said }.Where(s => !string.IsNullOrEmpty(s))), _now());
        if (_untyped.Length == 0)
        {
            _indicator.Hide();
            return;
        }

        if (!_host.IsDictationTargetFocused(target))
        {
            var app = _host.DictationAppName();
            Fail(_host.CopyDictatedText(_untyped)
                ? Localizer.Instance.Format("Diag_DictationFocusChanged", app)
                : Localizer.Instance.Format("Diag_FocusChanged", app));
            return;
        }

        if (_host.DictationBlockedReason() is { } blocked)
        {
            Fail(blocked);
            return;
        }

        _indicator.Hide();
        if (_host.InsertDictatedText(_untyped))
            _untyped = "";
    }

    private void OnLevel(float level)
    {
        if (level > _peak)
            _peak = level;
        if (level >= SpeechLevel)
            Interlocked.Exchange(ref _lastLoudTicks, _now().Ticks);
        _indicator.SetLevel(level);
    }

    private void Fail(string message)
    {
        Current = State.Idle;
        _host.DictationListening = false;
        _host.ReportDictation(message);
        _indicator.ShowMessage(message);
    }

    public void Dispose()
    {
        _watchdog?.Stop();
        _host.DictationHotkeyPressed -= Press;
        _host.DictationHotkeyReleased -= Release;
        _host.DictationCancelPressed -= Cancel;
        _audio.Level -= OnLevel;
        if (Current == State.Listening)
            _audio.Stop();
        _dictation?.Cancel();
    }
}
