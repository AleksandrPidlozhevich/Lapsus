using System;
using System.Text.Json.Serialization;
using Lapsus.Core.Layout;
using Lapsus.Core.Models;

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

    public string[] ExcludedWords { get; set; } = [];

    public string[] ExcludedApps { get; set; } = [];
}
