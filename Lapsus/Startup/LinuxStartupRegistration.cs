using System;
using System.IO;
using System.Runtime.Versioning;

namespace Lapsus.Startup;

[SupportedOSPlatform("linux")]
internal sealed class LinuxStartupRegistration : IStartupRegistration
{
    public bool IsSupported => true;

    public bool IsEnabled => StartupRegistrationFiles.IsLinuxAutostartEnabled(DesktopFilePath());

    public void SetEnabled(bool enabled)
    {
        var path = DesktopFilePath();
        if (!enabled)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, StartupRegistrationFiles.BuildLinuxDesktopContent(exe));
    }

    internal static string DesktopFilePath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(configHome, "autostart", StartupRegistrationFiles.LinuxDesktopFileName);
    }
}
