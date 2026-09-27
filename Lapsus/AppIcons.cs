using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Svg.Skia;
using SkiaSharp;

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

    public static WindowIcon CreateTrayTemplateIcon(int size = 256)
    {
        return CreateWindowIcon("/Assets/tray-icon-template.svg", size);
    }

    public static WindowIcon CreateWindowIcon(string assetPath, int size = 256)
    {
        using var source = Load(assetPath);
        using var png = RasterizeToPng(source, size);
        return new WindowIcon(png);
    }

    private static SvgSource Load(string assetPath)
    {
        var relative = assetPath.TrimStart('/');
        using var stream = AssetLoader.Open(new Uri(BaseUri, relative));
        return SvgSource.LoadFromStream(stream);
    }

    private static MemoryStream RasterizeToPng(SvgSource source, int size)
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

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = new MemoryStream();
        data.SaveTo(png);
        png.Position = 0;
        return png;
    }
}
