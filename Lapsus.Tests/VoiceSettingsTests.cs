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
    public void Each_choice_is_saved()
    {
        var voice = Voice();

        voice.SelectedLanguage = voice.LanguageOptions.First(o => o.Code == "uk");
        voice.SelectedModel = voice.ModelOptions.First(o => o.Model.Id == "whisper-small-q5");
        voice.SelectedUnload = voice.UnloadOptions.First(o => o.Value == SpeechUnloadAfter.Never);
        voice.KeepEnglishTermsLatin = false;

        var saved = Saved();
        Assert.Equal("uk", saved.DictationLanguage);
        Assert.Equal("whisper-small-q5", saved.SpeechModelId);
        Assert.Equal(SpeechUnloadAfter.Never, saved.SpeechUnloadAfter);
        Assert.False(saved.KeepEnglishTermsLatin);
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
