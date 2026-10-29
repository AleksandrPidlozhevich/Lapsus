using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Svg.Skia;
using SkiaSharp;
using System;
using System.IO;

namespace Lapsus;

internal static class AppIcons
{
    private static readonly Uri BaseUri = new("avares://Lapsus/");

    public static string PathForTheme(ThemeVariant variant)
    {
        return variant == ThemeVariant.Light ? "/Assets/icon-light.svg" : "/Assets/icon-dark.svg";
    }

    public static string BannerPathForTheme(ThemeVariant variant)
    {
        return variant == ThemeVariant.Light ? "/Assets/theme-light.svg" : "/Assets/theme-dark.svg";
    }

    public static string ToggleIconPath(string baseName, ThemeVariant uiVariant)
    {
        var suffix = uiVariant == ThemeVariant.Light ? "light" : "dark";
        return $"/Assets/{baseName}-{suffix}.svg";
    }

    public static WindowIcon CreateWindowIcon(ThemeVariant variant, int size = 256)
    {
        return CreateWindowIcon(PathForTheme(variant), size);
    }

    // badge: a dot in the lower right corner, set while a downloaded update waits to be installed.
    public static WindowIcon CreateTrayIcon(ThemeVariant variant, bool badge)
    {
        return CreateWindowIcon(PathForTheme(variant), 256, badge);
    }

    public static WindowIcon CreateTrayTemplateIcon(bool badge = false)
    {
        return CreateWindowIcon("/Assets/tray-icon-template.svg", 256, badge);
    }

    public static WindowIcon CreateWindowIcon(string assetPath, int size = 256, bool badge = false)
    {
        using var source = Load(assetPath);
        using var png = RasterizeToPng(source, size, badge);
        return new WindowIcon(png);
    }

    private static SvgSource Load(string assetPath)
    {
        var relative = assetPath.TrimStart('/');
        using var stream = AssetLoader.Open(new Uri(BaseUri, relative));
        return SvgSource.LoadFromStream(stream);
    }

    private static readonly SKColor BadgeColor = new(0xdf, 0x53, 0x38);

    private static MemoryStream RasterizeToPng(SvgSource source, int size, bool badge)
    {
        var picture = source.Picture
                      ?? throw new InvalidOperationException("SVG failed to load.");

        var bounds = picture.CullRect;
        var scale = Math.Min(size / bounds.Width, size / bounds.Height);

        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate((size - bounds.Width * scale) / 2f, (size - bounds.Height * scale) / 2f);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);

        if (badge)
            DrawBadge(canvas, size);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = new MemoryStream();
        data.SaveTo(png);
        png.Position = 0;
        return png;
    }

    // The ring around the dot is cut out of the glyph so the dot stays readable at 16 px, and in the
    // macOS template icon, where only the alpha shows.
    private static void DrawBadge(SKCanvas canvas, int size)
    {
        canvas.ResetMatrix();
        var centre = size * 0.76f;
        var radius = size * 0.22f;

        using var cut = new SKPaint { BlendMode = SKBlendMode.Clear, IsAntialias = true };
        canvas.DrawCircle(centre, centre, radius + size * 0.06f, cut);

        using var dot = new SKPaint { Color = BadgeColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawCircle(centre, centre, radius, dot);
    }
}
