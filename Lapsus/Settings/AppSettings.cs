using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Core.Models;
using System;
using System.Text.Json.Serialization;

namespace Lapsus.Settings;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;

    public int HotkeyVirtualKey { get; set; } = 0x13;

    public int SelectionHotkeyVirtualKey { get; set; } = 0x91;

    public int CaseHotkeyVirtualKey { get; set; }

    public int TransliterateHotkeyVirtualKey { get; set; }

    public int ReverseRtlHotkeyVirtualKey { get; set; }

    public bool AutoMode { get; set; }

    public bool AutoFixTypos { get; set; }

    public bool SwitchSystemLayout { get; set; }

    public bool RunAtStartup { get; set; }

    public KeyboardLayout? PreferredLayout { get; set; }

    public bool SkipPasswordFields { get; set; } = true;

    public bool ShowLayoutIndicator { get; set; }

    public string InterfaceLanguage { get; set; } = "en";

    public AppThemePreference ThemePreference { get; set; } = AppThemePreference.Dark;

    public bool NeuralCorrectionEnabled { get; set; }

    public bool PlainLayoutSwitch { get; set; }

    public string? SelectedModelId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ComputeDevicePreference>))]
    public ComputeDevicePreference ComputeDevice { get; set; } = ComputeDevicePreference.Auto;

    public DateOnly? OrganizationDetectedOn { get; set; }

    // macOS: the first-launch explanation of what Lapsus reads was answered (OnboardingWindow).
    public bool OnboardingDone { get; set; }

    // Mac App Store edition: the day the trial of the paid features began (StoreAccessPolicy).
    public DateOnly? StoreTrialStartedOn { get; set; }

    // Voice typing: the hotkey starts and stops recording; the speech model loads on first use and is unloaded
    // after SpeechUnloadAfter of no dictation.
    public bool DictationEnabled { get; set; }

    // Option twice on a Mac: Control twice is one of macOS's own Dictation shortcuts.
    public int DictationHotkey { get; set; } =
        HotkeyTriggers.DoubleTap(OperatingSystem.IsMacOS() ? TapModifier.Alt : TapModifier.Control);

    public string SpeechModelId { get; set; } = Speech.SpeechModelCatalog.DefaultId;

    [JsonConverter(typeof(JsonStringEnumConverter<SpeechUnloadAfter>))]
    public SpeechUnloadAfter SpeechUnloadAfter { get; set; } = SpeechUnloadAfter.Min5;

    // A language code to always dictate in; null picks among the installed layouts' languages.
    public string? DictationLanguage { get; set; }

    public bool KeepEnglishTermsLatin { get; set; } = true;

    // The recording device by name (WinMM product name); null is the system default.
    public string? MicrophoneName { get; set; }

    // "Term" or "Term|how it sounds|…", one per entry.
    public string[] DictationGlossary { get; set; } = [];

    public string[] ExcludedWords { get; set; } = [];

    public string[] ExcludedApps { get; set; } = [];
}
