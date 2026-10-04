using System;
using System.IO;
using System.Linq;

namespace Lapsus.Input;

// Recognizes apps built on Chromium or Electron from their bundle layout. Only these need the
// AXManualAccessibility request, so native apps are never touched.
internal static class ChromiumApps
{
    private static readonly string[] FrameworkMarkers =
    [
        "Electron", "Chromium", "Chrome", "Brave", "Edge", "Vivaldi", "Opera"
    ];

    public static bool IsChromiumFrameworkName(string frameworkDirectoryName)
    {
        if (!frameworkDirectoryName.EndsWith(" Framework.framework", StringComparison.Ordinal))
            return false;

        return FrameworkMarkers.Any(marker =>
            frameworkDirectoryName.Contains(marker, StringComparison.Ordinal));
    }

    // The executable of an app bundle lives at <App>.app/Contents/MacOS/<name>.
    public static string? AppBundleFrameworks(string executablePath)
    {
        const string marker = ".app/";
        var index = executablePath.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var appRoot = executablePath[..(index + marker.Length - 1)];
        return Path.Combine(appRoot, "Contents", "Frameworks");
    }

    public static bool IsChromiumExecutable(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
            return false;

        var frameworks = AppBundleFrameworks(executablePath);
        if (frameworks is null || !Directory.Exists(frameworks))
            return false;

        return Directory.EnumerateDirectories(frameworks)
            .Select(Path.GetFileName)
            .Any(name => name is not null && IsChromiumFrameworkName(name));
    }
}
