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
        public event Action<bool>? DictationHotkeyReleased;
        public event Action? DictationCancelPressed;
        public event Action<string>? GlossaryTermSelected { add { } remove { } }

        public string? Blocked;
        public object Target = "notepad";
        public object Focused = "notepad";
        public readonly List<string> Typed = [];
        public readonly List<string> Copied = [];
        public readonly List<string> Reports = [];

        public void PressHotkey()
        {
            HoldHotkey();
            LetGo();
        }

        public void HoldHotkey() => DictationHotkeyPressed?.Invoke();
        public void LetGo(bool interrupted = false) => DictationHotkeyReleased?.Invoke(interrupted);
        public void PressEscape() => DictationCancelPressed?.Invoke();

        public string Before = "";

        public string? DictationBlockedReason() => Blocked;
        public string DictationTextBefore() => Before;
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

        public int Taken;

        public float[] TakeRecorded()
        {
            Taken++;
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
        public readonly Queue<string> Answers = new();
        public TaskCompletionSource<string>? Hold;
        public DictationRequest? LastRequest;

        public void Prepare() => Prepared++;

        public readonly List<DictationRequest> Requests = [];
        public readonly List<int> PcmLengths = [];
        public readonly List<CancellationToken> Tokens = [];
        public string? HeardLanguage;

        public async Task<DictationResult> TranscribeAsync(float[] pcm, DictationRequest request, CancellationToken ct)
        {
            LastRequest = request;
            Requests.Add(request);
            PcmLengths.Add(pcm.Length);
            Tokens.Add(ct);
            var text = Hold is { } hold
                ? await hold.Task.WaitAsync(ct)
                : Answers.TryDequeue(out var next) ? next : Answer;
            return new DictationResult(text, HeardLanguage ?? request.Language);
        }
    }

    private sealed class Badge : IDictationIndicator
    {
        public string State = "hidden";
        public string? Message;

        public string? Heard;

        public void ShowListening() => State = "listening";
        public void ShowHeard(string text) => Heard = text;
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
    public async Task Held_while_speaking_it_types_on_letting_go()
    {
        var rig = new Rig();

        rig.Host.HoldHotkey();
        Assert.Equal(DictationSession.State.Listening, rig.Session.Current);
        rig.Mic.Hear(0.1f);
        rig.Now += DictationSession.HoldToTalk;
        rig.Host.LetGo();
        await rig.Session.Pending!;

        Assert.Equal(["Закинул pull request."], rig.Host.Typed);
    }

    [Fact]
    public void A_short_press_keeps_listening_hands_free()
    {
        var rig = new Rig();

        rig.Host.HoldHotkey();
        rig.Now += DictationSession.HoldToTalk - TimeSpan.FromMilliseconds(100);
        rig.Host.LetGo();

        Assert.Equal(DictationSession.State.Listening, rig.Session.Current);
    }

    [Fact]
    public void Keys_that_turn_out_to_be_another_shortcut_throw_the_recording_away()
    {
        var rig = new Rig();

        rig.Host.HoldHotkey();
        rig.Now += TimeSpan.FromSeconds(1);
        rig.Host.LetGo(interrupted: true);

        Assert.Equal(DictationSession.State.Idle, rig.Session.Current);
        Assert.False(rig.Mic.IsRecording);
        Assert.Empty(rig.Host.Typed);
    }

    [Fact]
    public async Task A_hands_free_recording_stops_on_the_next_press_even_if_held()
    {
        var rig = new Rig();

        rig.Host.PressHotkey();
        rig.Mic.Hear(0.1f);
        rig.Host.HoldHotkey();
        rig.Now += TimeSpan.FromSeconds(2);
        rig.Host.LetGo(interrupted: true);
        await rig.Session.Pending!;

        Assert.Equal(["Закинул pull request."], rig.Host.Typed);
    }

    // Speak past PartAfter, then pause past PauseEndsPart: the part said so far is cut off.
    private static async Task SayAPartAsync(Rig rig)
    {
        rig.Mic.Hear(0.1f);
        rig.Now += DictationSession.PartAfter + TimeSpan.FromSeconds(1);
        rig.Mic.Hear(0.1f);
        rig.Now += DictationSession.PauseEndsPart + TimeSpan.FromMilliseconds(100);
        rig.Session.Tick();
        await rig.Session.Pending!;
    }

    // Speak past PartAfter, then fall quiet past EarlyAfter (not yet PauseEndsPart) and let the watchdog look.
    private static void PauseBrieflyAfterAPart(Rig rig)
    {
        rig.Mic.Hear(0.1f);
        rig.Now += DictationSession.PartAfter + TimeSpan.FromSeconds(1);
        rig.Mic.Hear(0.1f);
        rig.Now += DictationSession.EarlyAfter + TimeSpan.FromMilliseconds(50);
        rig.Session.Tick();
    }

    [Fact]
    public async Task A_part_is_recognised_while_the_pause_is_still_going_on()
    {
        var rig = new Rig();
        rig.Engine.Answers.Enqueue("Первая часть.");

        rig.Host.PressHotkey();
        PauseBrieflyAfterAPart(rig);
        Assert.Single(rig.Engine.Requests);
        Assert.Empty(rig.Host.Typed);

        rig.Now += DictationSession.PauseEndsPart;
        rig.Session.Tick();
        await rig.Session.Pending!;

        Assert.Equal(["Первая часть."], rig.Host.Typed);
        Assert.Single(rig.Engine.Requests);
    }

    [Fact]
    public async Task Speech_that_resumes_drops_the_early_text_but_keeps_its_audio()
    {
        var rig = new Rig();

        rig.Host.PressHotkey();
        PauseBrieflyAfterAPart(rig);
        rig.Mic.Hear(0.1f);
        rig.Session.Tick();
        Assert.True(rig.Engine.Tokens[0].IsCancellationRequested);

        rig.Host.PressHotkey();
        await rig.Session.Pending!;

        var part = rig.Mic.Recorded.Length;
        Assert.Equal([part, part * 2], rig.Engine.PcmLengths);
        Assert.Single(rig.Host.Typed);
    }

    [Fact]
    public async Task Letting_go_after_a_pause_types_the_early_text_without_another_pass()
    {
        var rig = new Rig();

        rig.Host.HoldHotkey();
        PauseBrieflyAfterAPart(rig);
        rig.Mic.Recorded = new float[16000];
        rig.Host.LetGo();
        await rig.Session.Pending!;

        Assert.Equal(["Закинул pull request."], rig.Host.Typed);
        Assert.Single(rig.Engine.Requests);
    }

    [Fact]
    public async Task Room_noise_between_phrases_still_ends_a_part()
    {
        var rig = new Rig();
        rig.Engine.Answers.Enqueue("Первая часть.");

        rig.Host.PressHotkey();
        rig.Mic.Hear(0.2f);
        rig.Now += DictationSession.PartAfter + TimeSpan.FromSeconds(1);
        rig.Mic.Hear(0.2f);
        // Above the fixed speech level, but a fifth of the voice: a fan, or the microphone's gain between words.
        for (var i = 0; i < 16; i++)
        {
            rig.Now += TimeSpan.FromMilliseconds(50);
            rig.Mic.Hear(0.03f);
        }
        rig.Session.Tick();
        await rig.Session.Pending!;

        Assert.Equal(["Первая часть."], rig.Host.Typed);
        Assert.Equal(DictationSession.State.Listening, rig.Session.Current);
    }

    [Fact]
    public async Task A_long_hands_free_dictation_is_typed_part_by_part_while_it_goes_on()
    {
        var rig = new Rig();
        rig.Engine.Answers.Enqueue("Первая часть.");
        rig.Engine.Answers.Enqueue("Вторая часть.");

        rig.Host.PressHotkey();
        await SayAPartAsync(rig);

        Assert.Equal(["Первая часть."], rig.Host.Typed);
        Assert.Equal(DictationSession.State.Listening, rig.Session.Current);

        rig.Mic.Hear(0.1f);
        rig.Host.PressHotkey();
        await rig.Session.Pending!;

        Assert.Equal(["Первая часть.", "Вторая часть."], rig.Host.Typed);
        Assert.Equal("Первая часть.", rig.Engine.LastRequest!.PreviousText);
    }

    [Fact]
    public async Task The_language_heard_in_the_first_part_is_kept_for_the_rest_of_the_dictation()
    {
        var rig = new Rig();
        rig.Engine.Answers.Enqueue("First part.");
        rig.Engine.Answers.Enqueue("Second part.");
        rig.Engine.HeardLanguage = "en";

        rig.Host.PressHotkey();
        await SayAPartAsync(rig);
        rig.Mic.Hear(0.1f);
        rig.Host.PressHotkey();
        await rig.Session.Pending!;

        Assert.Equal(["ru", "en"], rig.Engine.Requests[0].Languages);
        Assert.Equal("en", rig.Engine.Requests[1].Language);
        Assert.Equal(["en"], rig.Engine.Requests[1].Languages);

        // A new dictation detects again.
        rig.Host.PressHotkey();
        rig.Mic.Hear(0.1f);
        rig.Host.PressHotkey();
        await rig.Session.Pending!;
        Assert.Equal(["ru", "en"], rig.Engine.Requests[2].Languages);
    }

    [Fact]
    public async Task While_the_hotkey_is_held_parts_wait_and_are_typed_on_letting_go()
    {
        var rig = new Rig();
        rig.Engine.Answers.Enqueue("Первая часть.");
        rig.Engine.Answers.Enqueue("Вторая часть.");

        rig.Host.HoldHotkey();
        await SayAPartAsync(rig);
        Assert.Empty(rig.Host.Typed);
        Assert.Equal("Первая часть.", rig.Badge.Heard);

        rig.Host.LetGo();
        await rig.Session.Pending!;

        Assert.Equal(["Первая часть. Вторая часть."], rig.Host.Typed);
    }

    [Fact]
    public async Task Esc_keeps_the_parts_already_typed()
    {
        var rig = new Rig();
        rig.Engine.Answers.Enqueue("Первая часть.");

        rig.Host.PressHotkey();
        await SayAPartAsync(rig);
        rig.Host.PressEscape();

        Assert.Equal(["Первая часть."], rig.Host.Typed);
        Assert.Equal(DictationSession.State.Idle, rig.Session.Current);
    }

    [Fact]
    public void A_short_quiet_moment_does_not_cut_a_part()
    {
        var rig = new Rig();

        rig.Host.PressHotkey();
        rig.Mic.Hear(0.1f);
        rig.Now += DictationSession.PartAfter - DictationSession.PauseEndsPart - TimeSpan.FromMilliseconds(200);
        rig.Mic.Hear(0.1f);
        rig.Now += DictationSession.PauseEndsPart + TimeSpan.FromMilliseconds(100);
        rig.Session.Tick();

        Assert.Equal(0, rig.Mic.Taken);
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

    [Fact]
    public async Task A_dictation_that_lost_its_field_is_still_in_the_history()
    {
        var rig = new Rig();
        rig.Host.PressHotkey();
        rig.Mic.Hear(0.1f);
        rig.Host.Focused = "telegram";
        rig.Host.PressHotkey();
        await rig.Session.Pending!;

        Assert.Empty(rig.Host.Typed);
        Assert.Equal(["Закинул pull request."], rig.Session.History.Recent());
    }

    [Fact]
    public async Task What_was_typed_before_the_caret_is_the_context()
    {
        var rig = new Rig();
        rig.Host.Before = "Вчера я сказал коллеге, что ";

        await rig.DictateAsync();
        Assert.Equal("Вчера я сказал коллеге, что", rig.Engine.LastRequest!.PreviousText);

        // The typed line already holds the last dictation; the remembered one is only for a cleared line.
        rig.Host.Before = "Новая строка";
        await rig.DictateAsync();
        Assert.Equal("Новая строка", rig.Engine.LastRequest!.PreviousText);
    }
}

public class DictationHistoryTests
{
    [Fact]
    public void Keeps_the_text_half_an_hour_newest_first()
    {
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var history = new DictationHistory(() => now);

        history.Add("Первая.");
        now += TimeSpan.FromMinutes(10);
        history.Add("Вторая.");
        Assert.Equal(["Вторая.", "Первая."], history.Recent());

        now += DictationHistory.Keep - TimeSpan.FromMinutes(5);
        Assert.Equal(["Вторая."], history.Recent());

        now += TimeSpan.FromMinutes(10);
        Assert.Empty(history.Recent());
    }
}

public class DictationInsertTests
{
    private static FakeInputBackend Backend() => new FakeInputBackend(new TableCorrector()).Focused();

    [Fact]
    public void Text_dictated_mid_sentence_continues_it()
    {
        var backend = Backend();
        backend.Type("Я думаю, что");

        Assert.True(backend.InsertDictatedText("Мы пойдём."));

        Assert.Equal((0, " мы пойдём."), backend.Injected.Single());
        Assert.Equal("Я думаю, что мы пойдём.", backend.DictationTextBefore());
    }

    [Fact]
    public void The_correction_hotkey_takes_the_dictation_back_and_retypes_it()
    {
        var backend = Backend();
        backend.Type("Итак,");
        backend.InsertDictatedText("Первая часть.");
        backend.InsertDictatedText("Вторая часть.");

        backend.PressHotkey();
        Assert.Equal((" первая часть. Вторая часть.".Length, ""), backend.Injected[^1]);
        Assert.Equal("Итак,", backend.DictationTextBefore());

        backend.PressHotkey();
        Assert.Equal((0, " первая часть. Вторая часть."), backend.Injected[^1]);
        Assert.Equal("Итак, первая часть. Вторая часть.", backend.DictationTextBefore());
    }

    [Fact]
    public void A_key_typed_after_the_dictation_keeps_it()
    {
        var backend = Backend();
        backend.InsertDictatedText("Готово.");
        backend.Type(" ok");
        var injected = backend.Injected.Count;

        backend.PressHotkey();

        Assert.DoesNotContain(backend.Injected.Skip(injected), i => i.Text.Length == 0);
    }

    [Fact]
    public async Task The_glossary_hotkey_hands_over_the_selected_term()
    {
        var backend = Backend().Listening();
        var terms = new List<string>();
        backend.GlossaryTermSelected += terms.Add;

        backend.Selection = "  Kubernetes ";
        await backend.RunSelectionAction(Lapsus.Core.Text.SelectionAction.AddToGlossary);
        backend.Selection = "Two lines\nare not a term";
        await backend.RunSelectionAction(Lapsus.Core.Text.SelectionAction.AddToGlossary);

        Assert.Equal(["Kubernetes"], terms);
        Assert.Empty(backend.Injected);
        Assert.Equal(2, backend.Diagnostics.Count);
    }
}

