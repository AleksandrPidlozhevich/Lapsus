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

    public bool HotkeyCyclesLayout { get; set; }

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

    public bool DictationEnabled { get; set; }

    public int DictationHotkey { get; set; } = DictationChord;

    // Held while speaking, tapped for hands-free. Two modifiers next to each other at the bottom left, which do
    // nothing on their own: Ctrl+Win on Windows, ⌃⌥ on a Mac.
    public static int DictationChord => HotkeyCombo.Encode(0,
        HotkeyModifiers.Control | (OperatingSystem.IsMacOS() ? HotkeyModifiers.Alt : HotkeyModifiers.Meta));

    public bool DictationSounds { get; set; } = true;

    public bool DictationShowButton { get; set; } = true;

    public string SpeechModelId { get; set; } = Speech.SpeechModelCatalog.DefaultId;

    [JsonConverter(typeof(JsonStringEnumConverter<SpeechUnloadAfter>))]
    public SpeechUnloadAfter SpeechUnloadAfter { get; set; } = SpeechUnloadAfter.Min5;

    // Null picks among the installed layouts' languages.
    public string? DictationLanguage { get; set; }

    public bool KeepEnglishTermsLatin { get; set; } = true;

    // The recording device by name (WinMM product name); null is the system default.
    public string? MicrophoneName { get; set; }

    // "Term" or "Term|how it sounds|…", one per entry.
    public string[] DictationGlossary { get; set; } = [];

    public string? AnnouncedUpdateVersion { get; set; }

    public string[] ExcludedWords { get; set; } = [];

    public string[] ExcludedApps { get; set; } = [];
}
