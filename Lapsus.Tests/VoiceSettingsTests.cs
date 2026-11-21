using Lapsus.Settings;
using Lapsus.Speech;
using Lapsus.ViewModels;

namespace Lapsus.Tests;

public sealed class VoiceSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lapsus-voice-{Guid.NewGuid():N}");
    private readonly AppSettings _settings = new();
    private readonly SettingsStore _store;
    private readonly List<bool> _applied = [];
    private int _released;

    public VoiceSettingsTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new SettingsStore(Path.Combine(_dir, "settings.json"));
    }

    private VoiceViewModel Voice(bool supported = true) =>
        new(_settings, _store, new SpeechModelStore(directory: Path.Combine(_dir, "speech")), supported,
            enabled => _applied.Add(enabled), () => { }, () => _released++);

    private AppSettings Saved() => _store.Load();

    public void Dispose()
    {
        Directory.Delete(_dir, true);
    }

    [Theory]
    [InlineData(false, "ru,uk,en", SpeechModelCatalog.ParakeetId)]
    [InlineData(true, "ru,uk,en", SpeechModelCatalog.DefaultId)]
    [InlineData(false, "en,he", SpeechModelCatalog.DefaultId)]
    [InlineData(false, "be,ru", SpeechModelCatalog.DefaultId)]
    [InlineData(false, "", SpeechModelCatalog.DefaultId)]
    [InlineData(false, "en", SpeechModelCatalog.ParakeetEnglishId)]
    [InlineData(true, "en", SpeechModelCatalog.DefaultId)]
    [InlineData(false, "he,en", SpeechModelCatalog.HebrewId)]
    [InlineData(true, "he,en", SpeechModelCatalog.HebrewId)]
    [InlineData(false, "ka,ru,en", SpeechModelCatalog.GeorgianId)]
    [InlineData(true, "hy,ru", SpeechModelCatalog.ArmenianId)]
    [InlineData(false, "ru,ka", SpeechModelCatalog.DefaultId)]
    public void Parakeet_is_suggested_only_without_a_GPU_and_for_languages_it_hears(bool gpu, string languages, string expected)
    {
        var model = SpeechModelCatalog.Recommended(gpu, languages.Split(',', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(expected, model.Id);
    }

    // installed: the downloaded models besides the chosen one.
    [Theory]
    [InlineData(SpeechModelCatalog.GeorgianId, "ru", "", SpeechModelCatalog.GeorgianId)]
    [InlineData(SpeechModelCatalog.GeorgianId, "ru", "whisper-large-v3-turbo-q8", "whisper-large-v3-turbo-q8")]
    [InlineData(SpeechModelCatalog.GeorgianId, "ru", "whisper-large-v3-turbo-q8,parakeet-tdt-v3-int8", "parakeet-tdt-v3-int8")]
    [InlineData(SpeechModelCatalog.GeorgianId, "ka", "parakeet-tdt-v3-int8", SpeechModelCatalog.GeorgianId)]
    [InlineData("whisper-large-v3-turbo-q8", "ka", SpeechModelCatalog.GeorgianId, SpeechModelCatalog.GeorgianId)]
    [InlineData("whisper-large-v3-turbo-q8", "ru", SpeechModelCatalog.GeorgianId, "whisper-large-v3-turbo-q8")]
    [InlineData(SpeechModelCatalog.ParakeetId, "he", "whisper-large-v3-turbo-q8", "whisper-large-v3-turbo-q8")]
    public void A_dictation_runs_on_the_downloaded_model_for_its_layout(
        string chosen, string language, string installed, string expected)
    {
        var have = installed.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var model = SpeechModelCatalog.ForLanguage(SpeechModelCatalog.Find(chosen)!, language, m => have.Contains(m.Id));

        Assert.Equal(expected, model.Id);
    }

    [Fact]
    public void A_layout_with_a_model_of_its_own_offers_that_download()
    {
        var parakeet = SpeechModelCatalog.Find(SpeechModelCatalog.ParakeetId)!;
        var voice = new VoiceViewModel(_settings, _store, new SpeechModelStore(directory: Path.Combine(_dir, "speech")),
            true, _ => { }, () => { }, () => { }, layouts: () => ["ru", "uk", "ka"], recommend: _ => parakeet);

        // Parakeet hears ru and uk; ka gets NVIDIA's Georgian model, written without capitals.
        var row = Assert.Single(voice.LayoutModels);
        Assert.StartsWith("ქართული", row.Text);
        Assert.Contains(SpeechModelCatalog.Find(SpeechModelCatalog.GeorgianId)!.DisplayName, row.Text);
        Assert.NotNull(row.Download);
    }

    [Theory]
    [InlineData(8, 1)]
    [InlineData(15.9, 2)]
    [InlineData(32, 2)]
    public void A_second_model_stays_loaded_only_with_16_GB(double gigabytes, int models)
    {
        Assert.Equal(models, DictationEngine.Capacity((long)(gigabytes * (1L << 30))));
    }

    [Fact]
    public void Each_model_idles_by_its_own_last_use()
    {
        var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        var morning = now.AddHours(-4).Ticks;
        var justNow = now.AddSeconds(-30).Ticks;

        Assert.True(DictationEngine.IsIdle(morning, now, TimeSpan.FromMinutes(5)));
        Assert.False(DictationEngine.IsIdle(justNow, now, TimeSpan.FromMinutes(5)));
        Assert.True(DictationEngine.IsIdle(justNow, now, TimeSpan.Zero));
    }

    [Fact]
    public void The_suggestion_is_taken_while_no_model_is_downloaded()
    {
        var parakeet = SpeechModelCatalog.Find(SpeechModelCatalog.ParakeetId)!;
        new VoiceViewModel(_settings, _store, new SpeechModelStore(directory: Path.Combine(_dir, "speech")), true,
            _ => { }, () => { }, () => { }, layouts: () => ["ru"], recommend: _ => parakeet);

        Assert.Equal(SpeechModelCatalog.ParakeetId, _settings.SpeechModelId);
        Assert.Equal(SpeechModelCatalog.ParakeetId, Voice().SelectedModel?.Model.Id);
    }

    [Fact]
    public void Turning_it_on_is_saved_and_applied()
    {
        var voice = Voice();

        voice.Enabled = true;

        Assert.True(Saved().DictationEnabled);
        Assert.Equal([true], _applied);
    }

    [Fact]
    public void Without_a_microphone_backend_it_stays_off()
    {
        _settings.DictationEnabled = true;

        var voice = Voice(supported: false);

        Assert.False(voice.Enabled);
        Assert.False(voice.IsSupported);
    }

    [Fact]
    public void Locked_behind_Plus_it_shows_off_even_if_it_was_on()
    {
        _settings.DictationEnabled = true;

        var voice = new VoiceViewModel(_settings, _store, new SpeechModelStore(directory: Path.Combine(_dir, "speech")),
            true, enabled => _applied.Add(enabled), () => { }, () => _released++, locked: () => true);

        Assert.True(voice.Locked);
        Assert.False(voice.Enabled);
    }

    [Fact]
    public void A_term_added_from_a_selection_is_saved_once()
    {
        var voice = Voice();
        voice.GlossaryText = "GitHub";

        voice.AddGlossaryTerm("Kubernetes");
        voice.AddGlossaryTerm("Kubernetes");

        Assert.Equal(["GitHub", "Kubernetes"], Saved().DictationGlossary);
    }

    [Fact]
    public void A_learned_spelling_joins_its_term_once()
    {
        var voice = Voice();
        voice.GlossaryText = "Kubernetes|кубер";

        voice.AddGlossaryTerm("Kubernetes|кубнетис");
        voice.AddGlossaryTerm("Kubernetes|кубнетис");
        voice.AddGlossaryTerm("Slack|слэк");

        Assert.Equal(["Kubernetes|кубер|кубнетис", "Slack|слэк"], Saved().DictationGlossary);
    }

    [Fact]
    public void Each_choice_is_saved()
    {
        var voice = Voice();

        voice.SelectedLanguage = voice.LanguageOptions.First(o => o.Code == "uk");
        voice.SelectedModel = voice.ModelOptions.First(o => o.Model.Id == "whisper-small-q5");
        voice.SelectedUnload = voice.UnloadOptions.First(o => o.Value == SpeechUnloadAfter.Never);
        voice.KeepEnglishTermsLatin = false;
        voice.SpokenPunctuation = false;

        var saved = Saved();
        Assert.Equal("uk", saved.DictationLanguage);
        Assert.Equal("whisper-small-q5", saved.SpeechModelId);
        Assert.Equal(SpeechUnloadAfter.Never, saved.SpeechUnloadAfter);
        Assert.False(saved.KeepEnglishTermsLatin);
        Assert.False(saved.DictationSpokenPunctuation);
    }

    [Fact]
    public void Automatic_language_comes_first_and_saves_as_none()
    {
        _settings.DictationLanguage = "de";
        var voice = Voice();

        Assert.Equal("de", voice.SelectedLanguage!.Code);
        voice.SelectedLanguage = voice.LanguageOptions[0];

        Assert.Null(voice.LanguageOptions[0].Code);
        Assert.Null(Saved().DictationLanguage);
    }

    [Fact]
    public void Opening_the_card_does_not_rewrite_the_settings()
    {
        _settings.SpeechModelId = "whisper-large-v3-turbo-q5";
        _settings.SpeechUnloadAfter = SpeechUnloadAfter.Min15;

        var voice = Voice();

        Assert.Equal("whisper-large-v3-turbo-q5", voice.SelectedModel!.Model.Id);
        Assert.Equal(SpeechUnloadAfter.Min15, voice.SelectedUnload!.Value);
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json")));
        Assert.Empty(_applied);
    }

    [Fact]
    public void An_unplugged_microphone_stays_chosen()
    {
        _settings.MicrophoneName = "USB headset (gone)";

        var voice = Voice();

        Assert.Null(voice.MicrophoneOptions[0].Device);
        Assert.Equal("USB headset (gone)", voice.SelectedMicrophone!.Device);
    }

    [Fact]
    public void Removing_the_model_releases_it_first()
    {
        var voice = Voice();

        voice.RemoveCommand.Execute(null);

        Assert.Equal(1, _released);
        Assert.False(voice.ModelInstalled);
    }

    [Theory]
    [InlineData(SpeechUnloadAfter.Immediately, 0)]
    [InlineData(SpeechUnloadAfter.Min2, 2)]
    [InlineData(SpeechUnloadAfter.Min5, 5)]
    [InlineData(SpeechUnloadAfter.Min15, 15)]
    [InlineData(SpeechUnloadAfter.Hour1, 60)]
    public void Unload_choices_map_to_their_delay(SpeechUnloadAfter value, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), value.Delay());
    }

    [Fact]
    public void Never_has_no_delay()
    {
        Assert.Null(SpeechUnloadAfter.Never.Delay());
    }

    [Theory]
    [InlineData(1.0, 384)]
    [InlineData(5.0, 384)]
    [InlineData(12.0, 704)]
    public void A_short_phrase_gets_a_cut_encoder_window_on_the_cpu(double seconds, int frames)
    {
        Assert.Equal(frames, WhisperRecognizer.WindowFor(seconds));
    }

    [Fact]
    public void Long_audio_takes_the_whole_window()
    {
        Assert.Null(WhisperRecognizer.WindowFor(29.5));
    }
}
