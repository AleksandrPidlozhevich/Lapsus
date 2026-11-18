using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Input;
using Lapsus.Localization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lapsus.ViewModels;

// One row per macOS permission, all alike: what it is for, whether it is given, and one button that asks
// (or, once macOS has asked, opens the list in System Settings).
public sealed partial class PermissionItem : ObservableObject
{
    private readonly string _key;
    private readonly Action _asked;

    internal PermissionItem(PermissionKind kind, string key, Action asked)
    {
        Kind = kind;
        _key = key;
        _asked = asked;
    }

    internal PermissionKind Kind { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGranted), nameof(IsMissing), nameof(StatusText), nameof(ActionText))]
    private PermissionState _state = PermissionState.Granted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    private bool _isWanted;

    public bool IsGranted => State == PermissionState.Granted;

    public bool IsMissing => IsWanted && !IsGranted;

    public string Title => Localizer.Instance[$"Perm_{_key}"];

    public string Why => Localizer.Instance[$"Perm_{_key}_Why"];

    public string StatusText => Localizer.Instance[State switch
    {
        PermissionState.Granted => "Perm_Granted",
        PermissionState.Denied => "Perm_Denied",
        _ => "Perm_NotGranted"
    }];

    public string ActionText => Localizer.Instance[State == PermissionState.NotAsked ? "Perm_Allow" : "Perm_OpenSettings"];

    internal void RefreshText() => OnPropertyChanged(string.Empty);

    [RelayCommand]
    private void Allow()
    {
        SystemPermissions.Request(Kind);
        _asked();
    }
}

public sealed partial class PermissionsViewModel : ObservableObject
{
    // The answer comes from System Settings or a system dialog, neither of which tells Lapsus; it is read again
    // this often while something is missing, so a row turns green while the user is still in the list.
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);

    private readonly Func<bool> _microphoneWanted;
    private readonly DispatcherTimer _timer;

    public PermissionsViewModel(Func<bool> microphoneWanted)
    {
        _microphoneWanted = microphoneWanted;
        Items =
        [
            new PermissionItem(PermissionKind.InputMonitoring, "InputMonitoring", Refresh),
            new PermissionItem(PermissionKind.Accessibility, "Accessibility", Refresh),
            new PermissionItem(PermissionKind.Microphone, "Microphone", Refresh)
        ];
        _timer = new DispatcherTimer { Interval = Poll };
        _timer.Tick += (_, _) => Refresh();
        Localizer.Instance.LanguageChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            foreach (var item in Items)
                item.RefreshText();
        });
        Refresh();
    }

    public IReadOnlyList<PermissionItem> Items { get; }

    public bool AnyMissing => Items.Any(i => i.IsMissing);

    public bool CaptureGranted =>
        (!SystemPermissions.InputMonitoringNeeded || Item(PermissionKind.InputMonitoring).IsGranted)
        && Item(PermissionKind.Accessibility).IsGranted;

    // Raised once the keyboard permissions are both in place after one of them was missing.
    public event Action? CaptureBecameGranted;

    public void Refresh()
    {
        var hadCapture = CaptureGranted;
        var wasMissing = AnyMissing;
        foreach (var item in Items)
        {
            item.IsWanted = item.Kind switch
            {
                PermissionKind.Microphone => OperatingSystem.IsMacOS() && _microphoneWanted(),
                PermissionKind.InputMonitoring => SystemPermissions.InputMonitoringNeeded,
                _ => OperatingSystem.IsMacOS()
            };
            item.State = SystemPermissions.State(item.Kind);
        }

        if (wasMissing != AnyMissing)
            OnPropertyChanged(nameof(AnyMissing));

        if (AnyMissing)
            _timer.Start();
        else
            _timer.Stop();

        if (!hadCapture && CaptureGranted)
            CaptureBecameGranted?.Invoke();
    }

    private PermissionItem Item(PermissionKind kind) => Items.First(i => i.Kind == kind);
}
