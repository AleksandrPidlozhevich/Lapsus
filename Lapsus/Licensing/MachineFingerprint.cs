using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Lapsus.Licensing;

public static class MachineFingerprint
{

    private const string Salt = "lapsus.machine.v1";

    private static readonly Lazy<string> Value = new(Compute);

    public static string Current => Value.Value;

    private static string Compute()
    {
        try
        {
            var raw = ReadPlatformId();
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Salt + "\n" + raw.Trim()));

            return Convert.ToBase64String(digest, 0, 16)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // No fingerprint means no activation (reminder stays). Never grant an unverified license.
            return string.Empty;
        }
    }

    private static string? ReadPlatformId()
    {
        if (OperatingSystem.IsWindows())
            return ReadWindowsMachineGuid();

        return OperatingSystem.IsMacOS() ? ReadMacPlatformUuid() : null;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsMachineGuid()
    {
        using var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = view.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid") as string;
    }

    [SupportedOSPlatform("macos")]
    private static string? ReadMacPlatformUuid()
    {
        var output = RunTool("/usr/sbin/ioreg", "-rd1 -c IOPlatformExpertDevice");
        if (output is null)
            return null;

        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains("IOPlatformUUID", StringComparison.Ordinal))
                continue;

            var close = line.LastIndexOf('"');
            if (close <= 0)
                continue;

            var open = line.LastIndexOf('"', close - 1);
            if (open >= 0 && close - open > 1)
                return line[(open + 1)..close];
        }

        return null;
    }

    private static string? RunTool(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            if (process.WaitForExit(3000))
                return output;

            process.Kill(entireProcessTree: true);
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
