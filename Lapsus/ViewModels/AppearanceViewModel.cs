using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Localization;
using Lapsus.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lapsus.ViewModels;

public sealed partial class AppearanceViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;

    public AppearanceViewModel(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        _selectedInterfaceLanguage = Languages.FirstOrDefault(l => l.Code == settings.InterfaceLanguage)
                                     ?? Languages[0];
        _themePreference = settings.ThemePreference;
        if (Application.Current is { } app)
        {
            _uiThemeVariant = app.ActualThemeVariant;
            app.ActualThemeVariantChanged += OnUiThemeChanged;
        }
    }

    public IReadOnlyList<LanguageOption> Languages => Localizer.Instance.Languages;

    [ObservableProperty] private LanguageOption _selectedInterfaceLanguage;

    [ObservableProperty] private AppThemePreference _themePreference;

    private ThemeVariant _uiThemeVariant = ThemeVariant.Dark;

    public bool IsLightTheme => ThemePreference == AppThemePreference.Light;

    public bool IsDarkTheme => ThemePreference == AppThemePreference.Dark;

    public bool IsSystemTheme => ThemePreference == AppThemePreference.System;

    public string SunIconPath => AppIcons.ToggleIconPath("sun", _uiThemeVariant);

    public string MoonIconPath => AppIcons.ToggleIconPath("moon", _uiThemeVariant);

    public string SystemIconPath => AppIcons.ToggleIconPath("system", _uiThemeVariant);

    public string TrashIconPath => AppIcons.ToggleIconPath("trash", _uiThemeVariant);

    [RelayCommand]
    private void SetTheme(string mode)
    {
        ThemePreference = mode switch
        {
            "Light" => AppThemePreference.Light,
            "Dark" => AppThemePreference.Dark,
            _ => AppThemePreference.System
        };
    }
    partial void OnSelectedInterfaceLanguageChanged(LanguageOption value)
    {
        _settings.InterfaceLanguage = value.Code;
        _store.Save(_settings);
        Localizer.Instance.SetLanguage(value.Code);
    }

    partial void OnThemePreferenceChanged(AppThemePreference value)
    {
        _settings.ThemePreference = value;
        _store.Save(_settings);
        if (Application.Current is { } app)
            ThemeApplier.Apply(app, value);
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsSystemTheme));
    }

    private void OnUiThemeChanged(object? sender, EventArgs e)
    {
        if (Application.Current is not { } app)
            return;

        _uiThemeVariant = app.ActualThemeVariant;
        OnPropertyChanged(nameof(SunIconPath));
        OnPropertyChanged(nameof(MoonIconPath));
        OnPropertyChanged(nameof(SystemIconPath));
        OnPropertyChanged(nameof(TrashIconPath));
    }
}
