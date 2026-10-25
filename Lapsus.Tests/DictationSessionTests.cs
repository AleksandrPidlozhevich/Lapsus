using Lapsus.Audio;
using Lapsus.Input.Dictation;

namespace Lapsus.Tests;

public class DictationSessionTests
{
    private sealed class Host : IDictationHost
    {
        public int DictationHotkey { get; set; }
        public bool DictationListening { get; set; }
        public event Action? DictationHotkeyPressed;
        public event Action? DictationCancelPressed;

        public string? Blocked;
        public object Target = "notepad";
        public object Focused = "notepad";
        public readonly List<string> Typed = [];
        public readonly List<string> Copied = [];
        public readonly List<string> Reports = [];

        public void PressHotkey() => DictationHotkeyPressed?.Invoke();
        public void PressEscape() => DictationCancelPressed?.Invoke();

        public string? DictationBlockedReason() => Blocked;
        public object CaptureDictationTarget() => Target;
        public bool IsDictationTargetFocused(object target) => Equals(target, Focused);

        public bool InsertDictatedText(string text)
        {
            Typed.Add(text);
            return true;
        }

        public bool CopyDictatedText(string text)
        {
            Copied.Add(text);
            return true;
        }

        public string? ActiveLanguage() => "ru";
        public IReadOnlyList<string> InstalledLanguages() => ["ru", "en"];
        public string DictationAppName() => "Notepad";
        public void ReportDictation(string message) => Reports.Add(message);
    }

    private sealed class Microphone : IAudioCapture
    {
        public bool IsRecording { get; private set; }
        public event Action<float>? Level;
        public Exception? StartFails;
        public float[] Recorded = Speech(2);
        public int Starts;
        public MicrophoneAccess Access { get; set; } = MicrophoneAccess.Granted;
        public int Requests;
        public int SettingsOpened;

        public void RequestAccess() => Requests++;
        public void OpenAccessSettings() => SettingsOpened++;

        public void Start()
        {
            if (StartFails is not null)
                throw StartFails;
            Starts++;
            IsRecording = true;
        }

        public float[] Stop()
        {
            IsRecording = false;
            return Recorded;
        }

        public void Hear(float level) => Level?.Invoke(level);

        public void Dispose()
        {
        }
    }

    private sealed class Engine : IDictationEngine
    {
        public bool HasModel { get; set; } = true;
        public int Prepared;
        public string Answer = "Закинул pull request.";
        public TaskCompletionSource<string>? Hold;
        public DictationRequest? LastRequest;

        public void Prepare() => Prepared++;

        public Task<string> TranscribeAsync(float[] pcm, DictationRequest request, CancellationToken ct)
        {
            LastRequest = request;
            if (Hold is { } hold)
                return hold.Task.WaitAsync(ct);
            return Task.FromResult(Answer);
        }
    }

    private sealed class Badge : IDictationIndicator
    {
        public string State = "hidden";
        public string? Message;

        public void ShowListening() => State = "listening";
        public void SetLevel(float level) { }
        public void ShowWorking() => State = "working";

        public void ShowMessage(string message)
        {
            State = "message";
            Message = message;
        }

        public void Hide() => State = "hidden";
    }

    private static float[] Speech(double seconds) =>
        Enumerable.Range(0, (int)(16000 * seconds)).Select(i => (float)Math.Sin(i * 0.05) * 0.2f).ToArray();

    private sealed class Rig
    {
        public readonly Host Host = new();
        public readonly Microphone Mic = new();
        public readonly Engine Engine = new();
        public readonly Badge Badge = new();
        public bool Enabled = true;
        public DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        public readonly DictationSession Session;

        public Rig()
        {
            Session = new DictationSession(Host, Mic, Engine, Badge, () => Enabled, () => Now, watchdog: false);
        }

        // Start, say something, stop, and let the transcription finish.
        public async Task DictateAsync()
        {
            Host.PressHotkey();
            Mic.Hear(0.1f);
            Host.PressHotkey();
            await Session.Pending!;
        }
    }

    [Fact]
    public async Task Press_listen_press_types_the_text_into_the_field()
    {
        var rig = new Rig();

        rig.Host.PressHotkey();
        Assert.Equal(DictationSession.State.Listening, rig.Session.Current);
        Assert.True(rig.Mic.IsRecording);
        Assert.True(rig.Host.DictationListening);
        Assert.Equal("listening", rig.Badge.State);
        Assert.Equal(1, rig.Engine.Prepared);

        rig.Mic.Hear(0.1f);
        rig.Host.PressHotkey();
        await rig.Session.Pending!;

        Assert.Equal(["Закинул pull request."], rig.Host.Typed);
        Assert.Equal(DictationSession.State.Idle, rig.Session.Current);
        Assert.False(rig.Host.DictationListening);
        Assert.Equal("hidden", rig.Badge.State);
        Assert.Equal("ru", rig.Engine.LastRequest!.Language);
        Assert.Equal(["ru", "en"], rig.Engine.LastRequest.Languages);
    }

    [Fact]
    public void Esc_while_listening_throws_the_recording_away()
    {
        var rig = new Rig();

        rig.Host.PressHotkey();
        rig.Host.PressEscape();

        Assert.Equal(DictationSession.State.Idle, rig.Session.Current);
        Assert.False(rig.Mic.IsRecording);
        Assert.False(rig.Host.DictationListening);
        Assert.Empty(rig.Host.Typed);
        Assert.Equal("hidden", rig.Badge.State);
    }

    [Fact]
    public async Task Esc_while_transcribing_cancels_it()
    {
        var rig = new Rig();
        rig.Engine.Hold = new TaskCompletionSource<string>();

        rig.Host.PressHotkey();
        rig.Mic.Hear(0.1f);
        rig.Host.PressHotkey();
        Assert.Equal(DictationSession.State.Transcribing, rig.Session.Current);
        rig.Host.PressEscape();
        await rig.Session.Pending!;

        Assert.Empty(rig.Host.Typed);
        Assert.Equal(DictationSession.State.Idle, rig.Session.Current);
    }

    [Fact]
    public async Task A_press_while_transcribing_does_not_start_another_recording()
    {
        var rig = new Rig();
        rig.Engine.Hold = new TaskCompletionSource<string>();

        rig.Host.PressHotkey();
        rig.Mic.Hear(0.1f);
        rig.Host.PressHotkey();
        rig.Host.PressHotkey();

        Assert.Equal(1, rig.Mic.Starts);
        rig.Engine.Hold.SetResult("Готово.");
        await rig.Session.Pending!;
        Assert.Equal(["Готово."], rig.Host.Typed);
    }

    [Fact]
    public async Task Text_for_a_field_that_lost_focus_goes_to_the_clipboard()
    {
        var rig = new Rig();

        rig.Host.PressHotkey();
        rig.Mic.Hear(0.1f);
        rig.Host.Focused = "telegram";
        rig.Host.PressHotkey();
        await rig.Session.Pending!;

        Assert.Empty(rig.Host.Typed);
        Assert.Equal(["Закинул pull request."], rig.Host.Copied);
        Assert.Single(rig.Host.Reports);
        Assert.Equal("message", rig.Badge.State);
    }

    [Fact]
    public void A_password_field_never_starts_a_recording()
    {
        var rig = new Rig();
        rig.Host.Blocked = "password field";

        rig.Host.PressHotkey();

        Assert.Equal(DictationSession.State.Idle, rig.Session.Current);
        Assert.Equal(0, rig.Mic.Starts);
        Assert.Equal(["password field"], rig.Host.Reports);
    }

    [Fact]
    public void Without_a_model_it_says_so_and_does_not_open_the_microphone()
    {
        var rig = new Rig();
        rig.Engine.HasModel = false;

        rig.Host.PressHotkey();

        Assert.Equal(0, rig.Mic.Starts);
        Assert.Equal(0, rig.Engine.Prepared);
        Assert.Equal("message", rig.Badge.State);
    }

    [Fact]
    public void A_missing_microphone_is_reported()
    {
        var rig = new Rig();
        rig.Mic.StartFails = new AudioCaptureException("no device");

        rig.Host.PressHotkey();

        Assert.Equal(DictationSession.State.Idle, rig.Session.Current);
        Assert.Contains(rig.Host.Reports, r => r.Contains("no device"));
        Assert.False(rig.Host.DictationListening);
    }

    [Fact]
    public void Before_macOS_has_asked_it_raises_the_permission_dialog_instead_of_recording()
    {
        var rig = new Rig();
        rig.Mic.Access = MicrophoneAccess.NotAsked;

        rig.Host.PressHotkey();

        Assert.Equal(1, rig.Mic.Requests);
        Assert.Equal(0, rig.Mic.Starts);
        Assert.Equal(DictationSession.State.Idle, rig.Session.Current);
        Assert.Equal("message", rig.Badge.State);
    }

    [Fact]
    public void A_denied_microphone_opens_its_settings_and_does_not_record()
    {
        var rig = new Rig();
        rig.Mic.Access = MicrophoneAccess.Denied;

        rig.Host.PressHotkey();

        Assert.Equal(1, rig.Mic.SettingsOpened);
        Assert.Equal(0, rig.Mic.Starts);
        Assert.Single(rig.Host.Reports);
    }

    [Fact]
    public async Task A_microphone_that_gives_only_zeros_is_reported_and_nothing_is_typed()
    {
        var rig = new Rig();
        rig.Mic.Recorded = new float[16000 * 2];

        rig.Host.PressHotkey();
        rig.Mic.Hear(0f);
        rig.Host.PressHotkey();
        await rig.Session.Pending!;

        Assert.Empty(rig.Host.Typed);
        Assert.Null(rig.Engine.LastRequest);
        Assert.Single(rig.Host.Reports);
    }

    [Fact]
    public async Task Nothing_heard_types_nothing()
    {
        var rig = new Rig();
        rig.Engine.Answer = "";

        await rig.DictateAsync();

        Assert.Empty(rig.Host.Typed);
        Assert.Equal("message", rig.Badge.State);
    }

    [Fact]
    public void Turned_off_the_hotkey_does_nothing()
    {
        var rig = new Rig { Enabled = false };

        rig.Host.PressHotkey();

        Assert.Equal(DictationSession.State.Idle, rig.Session.Current);
        Assert.Equal(0, rig.Mic.Starts);
    }

    [Fact]
    public async Task Long_silence_stops_the_recording_by_itself()
    {
        var rig = new Rig();

        rig.Host.PressHotkey();
        rig.Mic.Hear(0.1f);
        rig.Now += DictationSession.MaxSilence - TimeSpan.FromSeconds(1);
        rig.Session.Tick();
        Assert.Equal(DictationSession.State.Listening, rig.Session.Current);

        rig.Now += TimeSpan.FromSeconds(2);
        rig.Session.Tick();
        await rig.Session.Pending!;

        Assert.Equal(DictationSession.State.Idle, rig.Session.Current);
        Assert.Single(rig.Host.Typed);
    }

    [Fact]
    public async Task The_next_dictation_into_the_same_field_continues_the_last_one()
    {
        var rig = new Rig();

        await rig.DictateAsync();
        Assert.Null(rig.Engine.LastRequest!.PreviousText);

        await rig.DictateAsync();
        Assert.Equal("Закинул pull request.", rig.Engine.LastRequest!.PreviousText);

        rig.Host.Target = rig.Host.Focused = "telegram";
        await rig.DictateAsync();
        Assert.Null(rig.Engine.LastRequest!.PreviousText);
    }
}
