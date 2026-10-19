using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Localization;
using System;

namespace Lapsus.ViewModels;

// First launch on macOS, before any permission prompt: what Lapsus reads, what it never does with
// it, and which two permissions macOS is about to ask for.
public sealed partial class OnboardingViewModel
{
    private readonly Action _continue;
    private readonly Action _later;

    public OnboardingViewModel(Action onContinue, Action onLater)
    {
        _continue = onContinue;
        _later = onLater;
    }

    public Localizer L => Localizer.Instance;

    public FlowDirection FlowDirection =>
        Localizer.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    [RelayCommand]
    private void Continue() => _continue();

    [RelayCommand]
    private void Later() => _later();
}
