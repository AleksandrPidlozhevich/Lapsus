using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace Lapsus.Input;

// Only these need the AXManualAccessibility request, so native apps are never touched.
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

    // The executable of an app bundle lives at <App>.app/Contents/MacOS/<name>. Path.Combine on
    // Windows uses '\', so a bundle laid out in a test must be recognized with either separator.
    public static string? AppBundleFrameworks(string executablePath)
    {
        var slash = executablePath.IndexOf(".app/", StringComparison.Ordinal);
        var backslash = executablePath.IndexOf(".app\\", StringComparison.Ordinal);
        var index = slash < 0 ? backslash : backslash < 0 ? slash : Math.Min(slash, backslash);
        if (index < 0)
            return null;

        var appRoot = executablePath[..(index + ".app".Length)];
        return Path.Combine(appRoot, "Contents", "Frameworks");
    }

    private static readonly ConcurrentDictionary<string, bool> ByFrameworksDirectory = new(StringComparer.Ordinal);

    // Asked on the hotkey path and by the caret sampler; the disk is read once per bundle.
    public static bool IsChromiumExecutable(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
            return false;

        var frameworks = AppBundleFrameworks(executablePath);
        if (frameworks is null)
            return false;

        if (ByFrameworksDirectory.Count > 256)
            ByFrameworksDirectory.Clear();

        return ByFrameworksDirectory.GetOrAdd(frameworks, HasChromiumFramework);
    }

    // A bundle in a privacy-protected folder (Downloads, Desktop) cannot be listed without consent.
    private static bool HasChromiumFramework(string frameworks)
    {
        try
        {
            return Directory.Exists(frameworks)
                   && Directory.EnumerateDirectories(frameworks)
                       .Select(Path.GetFileName)
                       .Any(name => name is not null && IsChromiumFrameworkName(name));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
