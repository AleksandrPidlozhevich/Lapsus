using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Startup;

[SupportedOSPlatform("macos")]
internal sealed class MacOsStartupRegistration : IStartupRegistration
{
    public bool IsSupported => true;

    public bool IsEnabled => File.Exists(PlistFilePath());

    public void SetEnabled(bool enabled)
    {
        var path = PlistFilePath();
        if (!enabled)
        {
            TryBootout(path);
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, StartupRegistrationFiles.BuildMacOsPlistContent(exe), new UTF8Encoding(true));

        TryBootstrapBestEffort(path);
    }

    internal static string PlistFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", StartupRegistrationFiles.MacOsPlistFileName);
    }

    private static void TryBootstrapBestEffort(string path)
    {
        TryBootout(path);
        try
        {
            RunLaunchctl($"bootstrap {GetGuiDomain()} \"{path}\"");
        }
        catch (InvalidOperationException)
        {

        }
    }

    private static void TryBootout(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            RunLaunchctl($"bootout {GetGuiDomain()} \"{path}\"");
        }
        catch (InvalidOperationException)
        {

        }
    }

    [DllImport("libc", SetLastError = false)]
    private static extern uint getuid();

    private static string GetGuiDomain()
    {
        return $"gui/{getuid()}";
    }

    private static void RunLaunchctl(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/launchctl",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Failed to start launchctl.");

        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode == 0)
            return;

        var detail = string.IsNullOrWhiteSpace(error) ? arguments : error.Trim();
        throw new InvalidOperationException($"launchctl exited with code {process.ExitCode}: {detail}");
    }
}
