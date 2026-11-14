using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Localization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lapsus.ViewModels;

public enum OnboardingStep
{
    Welcome,

    // macOS only, and only while its two permissions are still to be asked for: the explanation has to
    // come before the system prompts (App Store guideline 2.5.14).
    Privacy,

    Dictionaries,

    Try,

    Options
}

// The introduction shown on a first launch, and again from Settings: what Lapsus does, the word lists it
// needs, a field to try it in, and the switches that decide how much happens unasked.
public sealed partial class OnboardingViewModel : ObservableObject
{
    // Keys that, typed on the English layout, spell a greeting on the other one.
    private static readonly Dictionary<string, string> Samples = new(StringComparer.OrdinalIgnoreCase)
    {
        ["uk"] = "dsnfyyz",
        ["ru"] = "ghbdtn",
        ["el"] = "kalhmera",
        ["he"] = "akuo",
        ["ar"] = "lvpfh",
        ["ka"] = "gamarjoba",
        ["ko"] = "dkssud"
    };

    // With Latin layouts only there is no other layout to cross into; a typo shows the same key at work.
    private const string TypoSample = "helo";

    private readonly Action _finish;
    private readonly IReadOnlyList<OnboardingStep> _steps;

    public OnboardingViewModel(SettingsViewModel settings, IEnumerable<string?> layoutLanguages, Action finish)
    {
        Settings = settings;
        _finish = finish;
        _steps = StepsFor(settings.OnboardingPending);
        Sample = SampleFor(layoutLanguages);
    }

    internal static IReadOnlyList<OnboardingStep> StepsFor(bool permissionsPending)
    {
        return Enum.GetValues<OnboardingStep>()
            .Where(step => step != OnboardingStep.Privacy || permissionsPending)
            .ToList();
    }

    internal static string SampleFor(IEnumerable<string?> layoutLanguages)
    {
        foreach (var language in layoutLanguages)
            if (language is not null && Samples.TryGetValue(language, out var sample))
                return sample;

        return TypoSample;
    }

    public SettingsViewModel Settings { get; }

    public Localizer L => Localizer.Instance;

    public FlowDirection FlowDirection =>
        Localizer.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string Sample { get; }

    [ObservableProperty] private int _index;

    // Every label below depends on the step, so one change announces them all.
    partial void OnIndexChanged(int value)
    {
        OnPropertyChanged(string.Empty);
    }

    public OnboardingStep Step => _steps[Index];

    public bool IsWelcome => Step == OnboardingStep.Welcome;

    public bool IsPrivacy => Step == OnboardingStep.Privacy;

    public bool IsDictionaries => Step == OnboardingStep.Dictionaries;

    public bool IsTry => Step == OnboardingStep.Try;

    public bool IsOptions => Step == OnboardingStep.Options;

    public bool CanGoBack => Index > 0;

    public string StepText => L.Format("Onb_Step", Index + 1, _steps.Count);

    public string TryText => L.Format("Onb_Try_Body", Sample, Settings.Shortcuts.SelectedHotkey.Name);

    public string NextText => L[IsPrivacy ? "Onb_Continue" : Index == _steps.Count - 1 ? "Onb_Done" : "Onb_Next"];

    [RelayCommand]
    private void Next()
    {
        // Past the explanation macOS may ask for its permissions, and capture starts.
        if (IsPrivacy)
            Settings.CompleteOnboarding();

        if (Index == _steps.Count - 1)
            _finish();
        else
            Index++;
    }

    [RelayCommand]
    private void Back()
    {
        if (CanGoBack)
            Index--;
    }
}
