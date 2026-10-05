using System;

namespace Lapsus.Input;

// Pids of one app are not one process. Cursor's renderer and Safari's web content are separate
// processes from the window the user clicked.
internal static class MacAppIdentity
{
    public static string? BundleRoot(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
            return null;

        var slash = executablePath.IndexOf(".app/", StringComparison.Ordinal);
        var backslash = executablePath.IndexOf(".app\\", StringComparison.Ordinal);
        var index = slash < 0 ? backslash : backslash < 0 ? slash : Math.Min(slash, backslash);
        if (index < 0)
            return null;

        return executablePath[..(index + ".app".Length)];
    }

    public static bool SameBundle(string? pathA, string? pathB)
    {
        var a = BundleRoot(pathA);
        var b = BundleRoot(pathB);
        return a is not null && string.Equals(a, b, StringComparison.Ordinal);
    }

    public static bool IsWebContent(string? executablePath)
    {
        return executablePath is not null
               && executablePath.Contains("com.apple.WebKit.WebContent", StringComparison.Ordinal);
    }

    public static bool IsWebKitHost(string? executablePath)
    {
        return IsWebContent(executablePath)
               || (executablePath is not null && executablePath.Contains("Safari.app", StringComparison.Ordinal));
    }
}
