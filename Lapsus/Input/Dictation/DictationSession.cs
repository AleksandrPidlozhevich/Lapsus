using Avalonia.Threading;
using Lapsus.Audio;
using Lapsus.Localization;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input.Dictation;

// One dictation at a time, driven by a single hotkey: press to start listening, press again to stop and type
// what was said into the field that had focus when it started. Esc cancels. The microphone opens at once and
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
    private CancellationTokenSource? _transcribing;
    private (object Target, string Text, DateTime At)? _previous;

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

        _host.DictationHotkeyPressed += Toggle;
        _host.DictationCancelPressed += Cancel;
        _audio.Level += OnLevel;

        if (watchdog)
        {
            _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _watchdog.Tick += (_, _) => Tick();
            _watchdog.Start();
        }
    }

    public State Current { get; private set; } = State.Idle;

    // The transcription in flight, for tests.
    internal Task? Pending { get; private set; }

    public void Toggle()
    {
        switch (Current)
        {
            case State.Idle:
                Start();
                break;
            case State.Listening:
                Pending = StopAsync();
                break;
        }
    }

    public void Cancel()
    {
        switch (Current)
        {
            case State.Listening:
                _audio.Stop();
                _host.DictationListening = false;
                Current = State.Idle;
                _indicator.Hide();
                break;
            case State.Transcribing:
                _transcribing?.Cancel();
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
        var previous = _previous is { } last && Equals(last.Target, target) && _now() - last.At < ContinueWithin
            ? last.Text
            : null;

        using var cancel = new CancellationTokenSource();
        _transcribing = cancel;
        string text;
        try
        {
            text = await _engine
                .TranscribeAsync(pcm, new DictationRequest(_language, _languages, previous), cancel.Token)
                .ConfigureAwait(true);
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
        finally
        {
            _transcribing = null;
        }

        Current = State.Idle;
        if (text.Length == 0)
        {
            _indicator.ShowMessage(Localizer.Instance["Diag_DictationNothingHeard"]);
            return;
        }

        if (!_host.IsDictationTargetFocused(target))
        {
            var app = _host.DictationAppName();
            Fail(_host.CopyDictatedText(text)
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
        if (_host.InsertDictatedText(text))
            _previous = (target, text, _now());
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
        _host.DictationHotkeyPressed -= Toggle;
        _host.DictationCancelPressed -= Cancel;
        _audio.Level -= OnLevel;
        if (Current == State.Listening)
            _audio.Stop();
        _transcribing?.Cancel();
    }
}
