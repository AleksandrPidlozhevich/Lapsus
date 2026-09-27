using Avalonia;
using Avalonia.Styling;
using Lapsus.Settings;

namespace Lapsus;

internal static class ThemeApplier
{
    public static void Apply(Application app, AppThemePreference preference)
    {
        app.RequestedThemeVariant = preference switch
        {
            AppThemePreference.Light => ThemeVariant.Light,
            AppThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
