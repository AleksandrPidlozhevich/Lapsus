using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Audio;
using Lapsus.Core.Input;
using Lapsus.Core.Speech;
using Lapsus.Input;
using Lapsus.Localization;
using Lapsus.Settings;
using Lapsus.Speech;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.ViewModels;

public sealed record VoiceModelOption(string Name, SpeechModel Model);

public sealed record VoiceLanguageOption(string Name, string? Code);

public sealed record MicrophoneOption(string Name, string? Device);

public sealed record UnloadOption(string Name, SpeechUnloadAfter Value);

// The Voice tab. Its hotkey lives with the others on the Hotkeys tab.
public partial class VoiceViewModel : ViewModelBase
{
    private static readonly TimeSpan GlossarySaveDelay = TimeSpan.FromMilliseconds(700);

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly SpeechModelStore _speech;
    private readonly Action<bool> _applyEnabled;
    private readonly Action _modelReady;
    private readonly Action _releaseModel;
    private readonly Func<bool> _locked;
    private readonly DispatcherTimer _saveGlossary;
    private CancellationTokenSource? _download;
    private bool _building;

    public VoiceViewModel(
        AppSettings settings, SettingsStore store, SpeechModelStore speech, bool supported,
        Action<bool> applyEnabled, Action modelReady, Action releaseModel, Func<bool>? locked = null)
    {
        _locked = locked ?? (() => false);
        _settings = settings;
        _store = store;
        _speech = speech;
        _applyEnabled = applyEnabled;
        _modelReady = modelReady;
        _releaseModel = releaseModel;
        IsSupported = supported;

        _saveGlossary = new DispatcherTimer { Interval = GlossarySaveDelay };
        _saveGlossary.Tick += (_, _) => SaveGlossary();

        _building = true;
        _enabled = supported && settings.DictationEnabled && !_locked();
        _keepEnglishTermsLatin = settings.KeepEnglishTermsLatin;
        _sounds = settings.DictationSounds;
        _showButton = settings.DictationShowButton;
        _glossaryText = string.Join(Environment.NewLine, settings.DictationGlossary);
        BuildOptions();
        _building = false;

        Localizer.Instance.LanguageChanged += (_, _) =>
        {
            BuildOptions();
            RefreshLabels();
        };
    }

    // Dictation needs a microphone backend (Windows and macOS).
    public bool IsSupported { get; }

    // The Mac App Store edition after its trial, without Lapsus Plus.
    public bool Locked => _locked();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownload), nameof(ShowUsage))]
    private bool _enabled;

    // The tab's two sections: the model and how to start it, or how speech is read.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMain), nameof(ShowUsage))]
    private bool _showRecognition;

    public bool ShowMain => !ShowRecognition;

    public bool ShowUsage => Enabled && !ShowRecognition;

    [RelayCommand]
    private void ShowSection(string section)
    {
        ShowRecognition = section == "Recognition";
        // A click on the section already shown unchecks its button; say again which one is on.
        OnPropertyChanged(nameof(ShowMain));
        OnPropertyChanged(nameof(ShowRecognition));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownload), nameof(ShowRemove))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(RemoveCommand))]
    private bool _busy;

    [ObservableProperty] private double _progress;

    [ObservableProperty] private string? _status;

    [ObservableProperty] private bool _keepEnglishTermsLatin;

    [ObservableProperty] private bool _sounds;

    [ObservableProperty] private bool _showButton;

    // The on-screen button needs a window that takes clicks without taking focus; only Windows has one here.
    public bool ButtonSupported => OperatingSystem.IsWindows();

    [ObservableProperty] private string _glossaryText;

    public ObservableCollection<VoiceModelOption> ModelOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelInstalled), nameof(ShowDownload), nameof(ShowRemove), nameof(ModelStatus),
        nameof(UnloadHint))]
    private VoiceModelOption? _selectedModel;

    public ObservableCollection<VoiceLanguageOption> LanguageOptions { get; } = [];

    [ObservableProperty] private VoiceLanguageOption? _selectedLanguage;

    public ObservableCollection<MicrophoneOption> MicrophoneOptions { get; } = [];

    [ObservableProperty] private MicrophoneOption? _selectedMicrophone;

    public ObservableCollection<UnloadOption> UnloadOptions { get; } = [];

    [ObservableProperty] private UnloadOption? _selectedUnload;

    private SpeechModel Model => SelectedModel?.Model ?? SpeechModelCatalog.Default;

    public bool ModelInstalled => _speech.IsInstalled(Model);

    public bool ShowDownload => Enabled && !ModelInstalled && !Busy;

    public bool ShowRemove => ModelInstalled && !Busy;

    public string ModelStatus => ModelInstalled
        ? Localizer.Instance.Format("Voice_ModelReady", Model.DisplayName)
        : Localizer.Instance.Format("Voice_ModelMissing", Model.SizeMb);

    public string Hint => Localizer.Instance["Voice_Hint"];

    // Shown once the model is on disk: how to use it, right where it was downloaded.
    // A double tap cannot be held, so it gets the press-speak-press version.
    public string TryIt => Localizer.Instance.Format(
        HotkeyTriggers.IsDoubleTap(_settings.DictationHotkey) ? "Voice_TryItTap" : "Voice_TryIt", HotkeyName);

    private string HotkeyName => _settings.DictationHotkey == 0
        ? Localizer.Instance["Voice_NoHotkey"]
        : HotkeyNames.Format(_settings.DictationHotkey);

    public string UnloadHint => Localizer.Instance.Format("Voice_UnloadHint", Model.RamMb);

    partial void OnEnabledChanged(bool value)
    {
        _settings.DictationEnabled = value;
        _store.Save(_settings);
        _applyEnabled(value);
        if (value)
            RefreshMicrophones();
    }

    partial void OnKeepEnglishTermsLatinChanged(bool value)
    {
        _settings.KeepEnglishTermsLatin = value;
        _store.Save(_settings);
    }

    partial void OnSoundsChanged(bool value)
    {
        _settings.DictationSounds = value;
        _store.Save(_settings);
    }

    partial void OnShowButtonChanged(bool value)
    {
        _settings.DictationShowButton = value;
        _store.Save(_settings);
        _applyEnabled(Enabled);
    }

    partial void OnGlossaryTextChanged(string value)
    {
        // Saved once typing pauses, not on every key.
        _saveGlossary.Stop();
        _saveGlossary.Start();
    }

    partial void OnSelectedModelChanged(VoiceModelOption? value)
    {
        if (_building || value is null || value.Model.Id == _settings.SpeechModelId)
            return;
        _settings.SpeechModelId = value.Model.Id;
        _store.Save(_settings);
        Status = null;
    }

    partial void OnSelectedLanguageChanged(VoiceLanguageOption? value)
    {
        if (_building || value is null)
            return;
        _settings.DictationLanguage = value.Code;
        _store.Save(_settings);
    }

    partial void OnSelectedMicrophoneChanged(MicrophoneOption? value)
    {
        if (_building || value is null)
            return;
        _settings.MicrophoneName = value.Device;
        _store.Save(_settings);
    }

    partial void OnSelectedUnloadChanged(UnloadOption? value)
    {
        if (_building || value is null)
            return;
        _settings.SpeechUnloadAfter = value.Value;
        _store.Save(_settings);
        _applyEnabled(Enabled);
    }

    [RelayCommand(CanExecute = nameof(CanChangeModel))]
    private async Task DownloadAsync()
    {
        Busy = true;
        Status = null;
        Progress = 0;
        _download = new CancellationTokenSource();
        var model = Model;
        try
        {
            var progress = new Progress<double>(p => Dispatcher.UIThread.Post(() => Progress = p));
            await _speech.DownloadAsync(model, progress, _download.Token).ConfigureAwait(true);
            // Load it once now: on a GPU the first run compiles shaders, which should not land on a dictation.
            if (Enabled)
                _modelReady();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Status = Localizer.Instance.Format("Voice_DownloadFailed", ex.Message);
        }
        finally
        {
            _download.Dispose();
            _download = null;
            Busy = false;
            RefreshLabels();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeModel))]
    private void Remove()
    {
        _releaseModel();
        try
        {
            _speech.Remove(Model);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
        RefreshLabels();
    }

    private bool CanChangeModel() => !Busy;

    public void CancelDownload() => _download?.Cancel();

    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Locked));
        OnPropertyChanged(nameof(ModelInstalled));
        OnPropertyChanged(nameof(ShowDownload));
        OnPropertyChanged(nameof(ShowRemove));
        OnPropertyChanged(nameof(ModelStatus));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(TryIt));
        OnPropertyChanged(nameof(UnloadHint));
    }

    private void SaveGlossary()
    {
        _saveGlossary.Stop();
        _settings.DictationGlossary = GlossaryText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _store.Save(_settings);
    }

    private void BuildOptions()
    {
        var building = _building;
        _building = true;
        try
        {
            ModelOptions.Clear();
            foreach (var model in SpeechModelCatalog.Available)
                ModelOptions.Add(new VoiceModelOption($"{model.DisplayName} — {model.SizeMb} MB", model));
            SelectedModel = ModelOptions.FirstOrDefault(o => o.Model.Id == _settings.SpeechModelId) ?? ModelOptions[0];

            LanguageOptions.Clear();
            LanguageOptions.Add(new VoiceLanguageOption(Localizer.Instance["Voice_LanguageAuto"], null));
            foreach (var code in DictionaryCatalog.Available.Select(d => d.Code).Distinct()
                         .Where(DictationLanguagePicker.Supports))
                LanguageOptions.Add(new VoiceLanguageOption(LanguageName(code), code));
            SelectedLanguage = LanguageOptions.FirstOrDefault(o => o.Code == _settings.DictationLanguage)
                               ?? LanguageOptions[0];

            UnloadOptions.Clear();
            foreach (var value in Enum.GetValues<SpeechUnloadAfter>())
                UnloadOptions.Add(new UnloadOption(UnloadName(value), value));
            SelectedUnload = UnloadOptions.First(o => o.Value == _settings.SpeechUnloadAfter);

            RefreshMicrophones();
        }
        finally
        {
            _building = building;
        }
    }

    // Devices come and go (a headset plugged in), so the list is read again when the card is turned on.
    private void RefreshMicrophones()
    {
        var building = _building;
        _building = true;
        try
        {
            MicrophoneOptions.Clear();
            MicrophoneOptions.Add(new MicrophoneOption(Localizer.Instance["Voice_MicDefault"], null));
            foreach (var name in AudioCaptureFactory.Microphones())
                MicrophoneOptions.Add(new MicrophoneOption(name, name));
            // A chosen device that is unplugged stays listed, so the choice is not lost while it is away.
            if (_settings.MicrophoneName is { } saved && MicrophoneOptions.All(o => o.Device != saved))
                MicrophoneOptions.Add(new MicrophoneOption(saved, saved));
            SelectedMicrophone = MicrophoneOptions.FirstOrDefault(o => o.Device == _settings.MicrophoneName)
                                 ?? MicrophoneOptions[0];
        }
        finally
        {
            _building = building;
        }
    }

    private static string LanguageName(string code)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(code).NativeName;
            return name.Length > 0 ? char.ToUpper(name[0], CultureInfo.GetCultureInfo(code)) + name[1..] : code;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    private static string UnloadName(SpeechUnloadAfter value) => value switch
    {
        SpeechUnloadAfter.Immediately => Localizer.Instance["Voice_UnloadImmediately"],
        SpeechUnloadAfter.Hour1 => Localizer.Instance["Voice_UnloadHour"],
        SpeechUnloadAfter.Never => Localizer.Instance["Voice_UnloadNever"],
        _ => Localizer.Instance.Format("Voice_UnloadMinutes", (int)value.Delay()!.Value.TotalMinutes)
    };
}
