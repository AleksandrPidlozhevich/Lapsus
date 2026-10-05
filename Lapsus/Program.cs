using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Lapsus.Startup;
using System;
using System.Runtime.Versioning;
using Velopack;

namespace Lapsus;

internal sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        CrashLog.Install();

        // Must run before the single-instance mutex or UI: Velopack re-invokes this exe with hook args.
        var velopack = VelopackApp.Build();

        if (OperatingSystem.IsWindows())
            ClearAutostartOnUninstall(velopack);

        velopack.Run();

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
            return 0;

        try
        {
            return BuildAvaloniaApp()
                .AfterSetup(_ =>
                {

                    if (OperatingSystem.IsMacOS())
                        MacOsDockVisibility.HideFromDock();

                    if (Application.Current is App app)
                        app.AttachSingleInstance(instance);
                })
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {

            ShutdownGuard.Arm();
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ClearAutostartOnUninstall(VelopackApp velopack)
        => velopack.OnBeforeUninstallFastCallback(_ => new WindowsStartupRegistration().SetEnabled(false));

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithBrandFonts()
            .LogToTrace();
}

internal static class BrandFonts
{
    private const string Collection = "fonts:Lapsus";

    private sealed class LapsusFontCollection() : EmbeddedFontCollection(
        new Uri(Collection, UriKind.Absolute),
        new Uri("avares://Lapsus/Assets/Fonts", UriKind.Absolute));

    public static AppBuilder WithBrandFonts(this AppBuilder builder)
        => builder
            .ConfigureFonts(fonts => fonts.AddFontCollection(new LapsusFontCollection()))
            .With(new FontManagerOptions
            {
                DefaultFamilyName = $"{Collection}#IBM Plex Sans",

                FontFallbacks =
                [
                    new FontFallback { FontFamily = new FontFamily($"{Collection}#IBM Plex Sans Hebrew") },
                    new FontFallback { FontFamily = new FontFamily($"{Collection}#IBM Plex Sans Arabic") },
                    new FontFallback { FontFamily = new FontFamily($"{Collection}#Noto Sans Georgian") },
                ],
            });
}
