using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Lapsus.Localization;
using Lapsus.ViewModels;

namespace Lapsus.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        _designWidth = Width;
        _designMinHeight = MinHeight;
        ApplyThemedAssets();
        PinToViewport(GeneralScroll, GeneralList);
        PinToViewport(AppsScroll, AppsList);
        PinToViewport(LibraryScroll, LibraryList);
        PinToViewport(AiScroll, AiList);
        PinToViewport(LicenseScroll, LicenseList);

        KeyDown += (_, e) =>
        {

            var isMacCloseChord = OperatingSystem.IsMacOS()
                                  && e.Key == Key.W && e.KeyModifiers == KeyModifiers.Meta;
            if (e.Key != Key.Escape && !isMacCloseChord)
                return;

            e.Handled = true;
            Close();
        };
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += OnThemeChanged;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            if (Application.Current is { } application)
                application.ActualThemeVariantChanged -= OnThemeChanged;
            Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        };
    }

    private readonly double _designWidth;
    private readonly double _designMinHeight;

    private const double MinScale = 0.7;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
        {

            var usableHeight = screen.WorkingArea.Height / screen.Scaling - 64;
            var usableWidth = screen.WorkingArea.Width / screen.Scaling - 48;

            var applied = (Root.LayoutTransform as ScaleTransform)?.ScaleY ?? 1;
            var natural = Bounds.Height > 0 ? Bounds.Height / applied : _designMinHeight;

            var scale = Math.Clamp(
                Math.Min(usableHeight / natural, usableWidth / _designWidth), MinScale, 1);

            Root.LayoutTransform = scale < 1 ? new ScaleTransform(scale, scale) : null;
            Width = _designWidth * scale;
            MinHeight = _designMinHeight * scale;
            MaxHeight = Math.Max(MinHeight, usableHeight);
        }

        ViewModel?.OnWindowReopened();
    }

    public void ShowLicensePage()
    {
        Tabs.SelectedIndex = Tabs.ItemCount - 1;
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void OnRunningAppsDropDownOpened(object? sender, EventArgs e)
    {
        ViewModel?.RefreshRunningApps();
    }

    private AboutWindow? _about;

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        if (_about is not null)
        {
            _about.Activate();
            return;
        }

        _about = new AboutWindow
        {
            DataContext = new AboutViewModel(viewModel.AppVersion, viewModel.License)
        };
        _about.Closed += (_, _) => _about = null;
        _about.Show(this);
    }

    private static void PinToViewport(ScrollViewer scroll, Control content)
    {
        scroll.SizeChanged += (_, e) =>
        {
            var inset = scroll.Padding.Left + scroll.Padding.Right + content.Margin.Left + content.Margin.Right;
            content.MaxWidth = Math.Max(0, e.NewSize.Width - inset);
        };
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyThemedAssets();
    }

    private void ApplyThemedAssets()
    {
        var variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        AppLogo.Path = AppIcons.BannerPathForTheme(variant);
        Icon = AppIcons.CreateWindowIcon(AppIcons.PathForTheme(variant));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {

        Dispatcher.UIThread.Post(() =>
        {
            var context = DataContext;
            DataContext = null;
            DataContext = context;
        });
    }
}
