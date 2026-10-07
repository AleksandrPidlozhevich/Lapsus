using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Licensing;
using Lapsus.Localization;
using Lapsus.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Lapsus.ViewModels;

public sealed record ComponentCredit(string Name, string License, string Url);

public sealed record DictionaryCredit(string Name, string License);

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly LicenseViewModel _license;

    public AboutViewModel(string appVersion, LicenseViewModel license)
    {
        AppVersion = appVersion;
        _license = license;
    }

    public Localizer L => Localizer.Instance;

    public FlowDirection FlowDirection =>
        Localizer.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string AppVersion { get; }

    public bool ShowDonate => !_license.HasLicense && !_license.ShowLicenseNotice;

    public IReadOnlyList<ComponentCredit> Components { get; } =
    [
        new("Avalonia 12.1.2", "MIT", "https://avaloniaui.net"),
        new("IBM Plex Sans", "SIL Open Font License 1.1", "https://github.com/IBM/plex"),
        new("Noto Sans Georgian", "SIL Open Font License 1.1", "https://github.com/notofonts/georgian"),
        new("CommunityToolkit.Mvvm 8.4.2", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("Svg.Controls.Skia.Avalonia 12.0.0.17", "MIT", "https://github.com/wieslawsoltes/Svg.Skia"),
        new("Velopack 1.2.0", "MIT", "https://github.com/velopack/velopack"),
        new(OnnxGenAiCredit, "MIT", "https://github.com/microsoft/onnxruntime-genai"),
        new("SymSpell 6.7.3", "MIT", "https://github.com/wolfgarbe/symspell"),
        new("WeCantSpell.Hunspell 7.0.1", "MPL 1.1", "https://github.com/aarondandy/WeCantSpell.Hunspell")
    ];

    public IReadOnlyList<DictionaryCredit> Dictionaries { get; } =
        DictionaryCatalog.Available
            .OrderBy(d => d.DisplayName, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true))
            .Select(d => new DictionaryCredit(d.Title, d.License))
            .ToList();

    private static string OnnxGenAiCredit => OperatingSystem.IsWindows()
        ? "ONNX Runtime GenAI (DirectML) 0.14.1"
        : "ONNX Runtime GenAI 0.15.2";

    [RelayCommand]
    private void Donate() => LicenseViewModel.OpenLink(LicenseLinks.Donate);

    [RelayCommand]
    private void OpenWebsite() => LicenseViewModel.OpenLink(LicenseLinks.Home);

    [RelayCommand]
    private void OpenRepo() => LicenseViewModel.OpenLink(LicenseLinks.Repo);

    [RelayCommand]
    private void OpenLicenseText() => LicenseViewModel.OpenLink(LicenseLinks.LicenseText);

    [RelayCommand]
    private void OpenThirdPartyNotices() => LicenseViewModel.OpenLink(LicenseLinks.ThirdPartyNotices);

    [RelayCommand]
    private void OpenComponent(string? url)
    {

        if (!string.IsNullOrWhiteSpace(url))
            LicenseViewModel.OpenLink(url);
    }
}
