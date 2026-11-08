using Avalonia.Threading;
using Lapsus.Audio;
using Lapsus.Localization;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input.Dictation;

// Runs on the UI thread. Text goes nowhere if focus moved to another field.
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

    // Speech is also measured against the speaker's own recent loudness: room noise, or a microphone's automatic
    // gain lifting it between words, stays above SpeechLevel, and a pause then never came (logged 20–46 s
    // dictations went out as one piece). The recent peak halves in about five seconds of 50 ms level reports.
    private const float PauseBelowPeak = 0.2f;
    private const float PeakDecay = 0.993f;

    private const int SampleRate = 16000;

    // A long dictation is typed part by part while it goes on: once this much has been said since the last
    // part, the next pause this long ends a part, which is recognised and typed while the user keeps talking.
    public static readonly TimeSpan PartAfter = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PauseEndsPart = TimeSpan.FromMilliseconds(700);

    // Recognition of a part starts this far into the pause, so that its text is ready (~0.4 s on a GPU) by the
    // time the pause is long enough to end it. Speech that resumes sooner throws that work away, never the audio.
    public static readonly TimeSpan EarlyAfter = TimeSpan.FromMilliseconds(300);

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
    private float _recentPeak;
    private (object Target, string Text, DateTime At)? _previous;

    // This dictation: Esc cancels it whole; what was already typed stays.
    private CancellationTokenSource? _dictation;
    private string? _continued;
    private string _typedBefore = "";
    private string _said = "";
    private string _untyped = "";
    private Task _parts = Task.CompletedTask;

    // A part cut at EarlyAfter whose recognition is under way, while the pause has yet to reach PauseEndsPart.
    private (Task<DictationResult> Result, CancellationTokenSource Cancel, float[] Pcm, long LoudAt)? _early;

    // Audio taken for an early part that speech then continued: it opens the next part.
    private float[] _carry = [];
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
        History = new DictationHistory(_now);

        _host.DictationHotkeyPressed += Press;
        _host.DictationHotkeyReleased += Release;
        _host.DictationCancelPressed += Cancel;
        _audio.Level += OnLevel;

        if (watchdog)
        {
            _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _watchdog.Tick += (_, _) => Tick();
            _watchdog.Start();
        }
    }

    public State Current { get; private set; } = State.Idle;

    public DictationHistory History { get; }

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
        DropEarlyIfSpeechResumed(lastLoud);
        var quiet = now - lastLoud;
        if (now - _startedAt >= MaxRecording || quiet >= MaxSilence)
            Pending = StopAsync();
        else if (now - _lastCut >= PartAfter && lastLoud > _lastCut)
        {
            if (quiet >= PauseEndsPart)
                CutPart(now);
            else if (quiet >= EarlyAfter && _early is null && _parts.IsCompleted)
                StartEarly(lastLoud);
        }
    }

    private void StartEarly(DateTime lastLoud)
    {
        var pcm = TakeAudio(_audio.TakeRecorded());
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_dictation!.Token);
        _early = (_engine.TranscribeAsync(pcm, Request(), cancel.Token), cancel, pcm, lastLoud.Ticks);
    }

    private void DropEarlyIfSpeechResumed(DateTime lastLoud)
    {
        if (_early is not { } early || early.LoudAt == lastLoud.Ticks)
            return;
        early.Cancel.Cancel();
        _carry = early.Pcm;
        _early = null;
    }

    private float[] TakeAudio(float[] taken)
    {
        if (_carry.Length == 0)
            return taken;
        var all = new float[_carry.Length + taken.Length];
        _carry.CopyTo(all, 0);
        taken.CopyTo(all, _carry.Length);
        _carry = [];
        return all;
    }

    // Parts are recognised and typed in order while recording goes on.
    private void CutPart(DateTime now)
    {
        _lastCut = now;
        var before = _parts;
        if (_early is { } early)
        {
            // What was said after the early cut stays in the microphone's buffer and opens the next part.
            _early = null;
            _parts = RecognisePartAsync(before, () => early.Result, _dictation!.Token);
        }
        else
        {
            var pcm = TakeAudio(_audio.TakeRecorded());
            _parts = RecognisePartAsync(before, () => _engine.TranscribeAsync(pcm, Request(), _dictation!.Token),
                _dictation!.Token);
        }
        Pending = _parts;
    }

    // Never throws: a failure is kept for the end of the dictation, which reports it if nothing was heard.
    private async Task RecognisePartAsync(Task before, Func<Task<DictationResult>> recognise, CancellationToken cancel)
    {
        await before.ConfigureAwait(true);
        if (cancel.IsCancellationRequested)
            return;

        try
        {
            Heard(await recognise().ConfigureAwait(true));
            TypeWhatIsReady();
            if (_untyped.Length > 0 && Current == State.Listening)
                _indicator.ShowHeard(_untyped);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _partFailed = ex;
        }
    }

    // What precedes the caret and everything recognised so far: Whisper's context for the next part. The typed line
    // already holds an earlier dictation into it; the remembered one stands in when a click cleared the line.
    private DictationRequest Request()
    {
        var before = _typedBefore.Trim().Length > 0 ? _typedBefore.Trim() : _continued;
        var context = string.Join(' ', new[] { before, _said }.Where(s => !string.IsNullOrEmpty(s)));
        return new DictationRequest(_language, _languages, context.Length > 0 ? context : null);
    }

    private void Heard(DictationResult result)
    {
        var text = result.Text;
        if (text.Length == 0)
            return;
        // Detection is a pass of its own (~0.25 s on a GPU); one per dictation is enough, a speaker rarely switches mid-way.
        _language = result.Language;
        _languages = [result.Language];
        _said = _said.Length > 0 ? _said + " " + text : text;
        _untyped = _untyped.Length > 0 ? _untyped + " " + text : text;
    }

    // While the hotkey is held, letters would arrive with Ctrl down and act as shortcuts, so typing waits.
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
        _typedBefore = _host.DictationTextBefore();
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
        _early = null;
        _carry = [];
        _partFailed = null;
        _dictation?.Dispose();
        _dictation = new CancellationTokenSource();
        Interlocked.Exchange(ref _lastLoudTicks, _startedAt.Ticks);
        _peak = 0;
        _recentPeak = 0;
        Current = State.Listening;
        _host.DictationListening = true;
        _indicator.ShowListening();
    }

    private async Task StopAsync()
    {
        DropEarlyIfSpeechResumed(new DateTime(Interlocked.Read(ref _lastLoudTicks), DateTimeKind.Utc));
        var early = _early;
        _early = null;
        var pcm = TakeAudio(_audio.Stop());
        // After an early part only the pause is left, unless a soft word hides in it.
        if (early is not null && Loudest(pcm) < SpeechLevel)
            pcm = [];
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
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await _parts.ConfigureAwait(true);
            CaptureLog.Write($"dictation: released, earlier parts took {clock.ElapsedMilliseconds} ms more");
            cancel.ThrowIfCancellationRequested();
            if (early is { } part)
                Heard(await part.Result.ConfigureAwait(true));
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
        History.Add(_said);
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

    // The loudest 50 ms, measured as the microphone's level reports are.
    private static float Loudest(float[] pcm)
    {
        const int window = SampleRate / 20;
        var loudest = 0f;
        for (var at = 0; at < pcm.Length; at += window)
        {
            var end = Math.Min(at + window, pcm.Length);
            double sum = 0;
            for (var i = at; i < end; i++)
                sum += pcm[i] * pcm[i];
            loudest = Math.Max(loudest, (float)Math.Sqrt(sum / (end - at)));
        }
        return loudest;
    }

    private void OnLevel(float level)
    {
        if (level > _peak)
            _peak = level;
        _recentPeak = Math.Max(level, _recentPeak * PeakDecay);
        if (level >= Math.Max(SpeechLevel, _recentPeak * PauseBelowPeak))
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
