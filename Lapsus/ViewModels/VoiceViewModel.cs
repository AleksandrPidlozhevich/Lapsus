using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Audio;
using Lapsus.Core.Input;
using Lapsus.Core.Speech;
using Lapsus.Input;
using Lapsus.Core.Models;
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

public sealed record VoiceDeviceOption(string Name, ComputeDevicePreference Value);

// A layout the chosen model does not hear, and the model its dictations run on. Download: null once it is on disk.
public sealed record LayoutModelOption(string Text, string DownloadLabel, IRelayCommand? Download);

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
    private readonly Func<string> _deviceLabel;
    private readonly Func<IReadOnlyList<string>> _layouts;
    private readonly Func<IReadOnlyList<string>, SpeechModel>? _recommend;
    private readonly DispatcherTimer _saveGlossary;
    private CancellationTokenSource? _download;
    private bool _building;

    public VoiceViewModel(
        AppSettings settings, SettingsStore store, SpeechModelStore speech, bool supported,
        Action<bool> applyEnabled, Action modelReady, Action releaseModel, Func<bool>? locked = null,
        Func<string>? deviceLabel = null, Func<IReadOnlyList<string>>? layouts = null,
        Func<IReadOnlyList<string>, SpeechModel>? recommend = null)
    {
        _layouts = layouts ?? (() => []);
        _recommend = recommend;
        // Until a model is on disk the pick follows the machine (Parakeet without a graphics card); once one is,
        // the user's own choice stands. Read once at start, so a choice made before downloading is not undone.
        if (recommend is not null && !SpeechModelCatalog.Available.Any(speech.IsInstalled))
            settings.SpeechModelId = recommend(_layouts()).Id;

        _deviceLabel = deviceLabel ?? (() => "");
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
        _spokenPunctuation = settings.DictationSpokenPunctuation;
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

    public bool IsSupported { get; }

    public bool Locked => _locked();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownload), nameof(ShowUsage))]
    private bool _enabled;

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

    [ObservableProperty] private bool _spokenPunctuation;

    [ObservableProperty] private bool _sounds;

    [ObservableProperty] private bool _showButton;

    // Needs a window that takes clicks without taking focus.
    public bool ButtonSupported => OperatingSystem.IsWindows();

    [ObservableProperty] private string _glossaryText;

    public ObservableCollection<VoiceModelOption> ModelOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelInstalled), nameof(ShowDownload), nameof(ShowRemove), nameof(ModelStatus),
        nameof(UnloadHint))]
    private VoiceModelOption? _selectedModel;

    public ObservableCollection<LayoutModelOption> LayoutModels { get; } = [];

    public ObservableCollection<VoiceLanguageOption> LanguageOptions { get; } = [];

    [ObservableProperty] private VoiceLanguageOption? _selectedLanguage;

    public ObservableCollection<MicrophoneOption> MicrophoneOptions { get; } = [];

    [ObservableProperty] private MicrophoneOption? _selectedMicrophone;

    public ObservableCollection<UnloadOption> UnloadOptions { get; } = [];

    [ObservableProperty] private UnloadOption? _selectedUnload;

    public ObservableCollection<VoiceDeviceOption> DeviceOptions { get; } = [];

    [ObservableProperty] private VoiceDeviceOption? _selectedDevice;

    // Where the loaded model actually runs (a GPU that failed falls back to the CPU); empty while not loaded.
    public string DeviceNow => _deviceLabel() is { Length: > 0 } label
        ? Localizer.Instance.Format("Voice_DeviceNow", label)
        : "";

    private SpeechModel Model => SelectedModel?.Model ?? SpeechModelCatalog.Default;

    public bool ModelInstalled => _speech.IsInstalled(Model);

    public bool ShowDownload => Enabled && !ModelInstalled && !Busy;

    public bool ShowRemove => ModelInstalled && !Busy;

    public string ModelStatus => ModelInstalled
        ? Localizer.Instance.Format("Voice_ModelReady", Model.DisplayName)
        : Localizer.Instance.Format("Voice_ModelMissing", Model.SizeMb);

    public string Hint => Localizer.Instance["Voice_Hint"];

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

    partial void OnSpokenPunctuationChanged(bool value)
    {
        _settings.DictationSpokenPunctuation = value;
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
        BuildLayoutModels();
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

    partial void OnSelectedDeviceChanged(VoiceDeviceOption? value)
    {
        if (_building || value is null || value.Value == _settings.DictationDevice)
            return;
        _settings.DictationDevice = value.Value;
        _store.Save(_settings);
        // A loaded model moves now, so the next dictation does not pay for the reload.
        if (_deviceLabel().Length > 0)
            _modelReady();
    }

    [RelayCommand(CanExecute = nameof(CanChangeModel))]
    private Task DownloadAsync() => DownloadModelAsync(Model);

    private async Task DownloadModelAsync(SpeechModel model)
    {
        if (Busy)
            return;
        Busy = true;
        Status = null;
        Progress = 0;
        _download = new CancellationTokenSource();
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
        OnPropertyChanged(nameof(DeviceNow));
        BuildLayoutModels();
    }

    // Each layout language whose dictations run on another model than the chosen one (DictationEngine picks the same
    // way), or should: one made for it (he, ka, hy), or one that hears it at all. Grouped by that model.
    private void BuildLayoutModels()
    {
        if (_recommend is null)
            return;
        var chosen = Model;
        SpeechModel? For(string language) =>
            SpeechModelCatalog.ForLanguage(chosen, language, _speech.IsInstalled) is var used && used != chosen ? used
            : SpeechModelCatalog.OwnModel(language) is { } own && own != chosen ? own
            : !chosen.Hears(language) ? _recommend([language])
            : null;
        var rows = _layouts()
            .Select(DictationLanguagePicker.Normalize)
            .Distinct()
            .Where(DictationLanguagePicker.Supports)
            .Select(l => (Language: l, Model: For(l)))
            .Where(r => r.Model is not null && r.Model != chosen)
            .GroupBy(r => r.Model!);

        LayoutModels.Clear();
        foreach (var group in rows)
        {
            var model = group.Key;
            var languages = string.Join(", ", group.Select(r => LanguageName(r.Language)));
            var name = $"{model.DisplayName} — {model.SizeMb} MB";
            LayoutModels.Add(_speech.IsInstalled(model)
                ? new LayoutModelOption(Localizer.Instance.Format("Voice_LayoutModelReady", languages, name), "", null)
                : new LayoutModelOption(Localizer.Instance.Format("Voice_LayoutModelMissing", languages, name),
                    Localizer.Instance["Action_Download"], new AsyncRelayCommand(() => DownloadModelAsync(model))));
        }
    }

    // entry: a term, or "term|heard" as learned from a correction; a heard form joins the term's line if it is listed.
    public void AddGlossaryTerm(string entry)
    {
        var parts = entry.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;
        var lines = GlossaryText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var at = lines.FindIndex(l => l.Split('|')[0].Trim() == parts[0]);
        if (at < 0)
            lines.Add(string.Join('|', parts));
        else
        {
            var listed = lines[at].Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            var added = parts.Skip(1).Where(h => !listed.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
            if (added.Count == 0)
                return;
            lines[at] = string.Join('|', listed.Concat(added));
        }
        GlossaryText = string.Join(Environment.NewLine, lines);
        SaveGlossary();
    }

    private void SaveGlossary()
    {
        _saveGlossary.Stop();
        _settings.DictationGlossary = GlossaryText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _store.Save(_settings);
        GlossaryChanged?.Invoke(_settings.DictationGlossary);
    }

    // The terms as saved: Lapsus.Core's word list leaves them alone too.
    public event Action<string[]>? GlossaryChanged;

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

            // Whisper only knows "GPU if it can" or "CPU", so Auto is the GPU choice.
            DeviceOptions.Clear();
            DeviceOptions.Add(new VoiceDeviceOption(Localizer.Instance["Voice_DeviceAuto"], ComputeDevicePreference.Auto));
            DeviceOptions.Add(new VoiceDeviceOption(Localizer.Instance["Model_DeviceCpu"], ComputeDevicePreference.Cpu));
            SelectedDevice = DeviceOptions.FirstOrDefault(o => o.Value == _settings.DictationDevice) ?? DeviceOptions[0];

            RefreshMicrophones();
            BuildLayoutModels();
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
            // Georgian has no capitals: ToUpper turns "ქართული" into Mtavruli, the all-caps display script.
            return name.Length > 0 && code != "ka"
                ? char.ToUpper(name[0], CultureInfo.GetCultureInfo(code)) + name[1..]
                : name.Length > 0 ? name : code;
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
