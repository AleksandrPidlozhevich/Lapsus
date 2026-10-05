using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Lapsus.Licensing;

public enum DeviceOwnership
{

    Unknown,

    Personal,

    Managed
}

// Reminder signal only, never a gate; result never leaves the device.
internal static class ManagedDeviceProbe
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(3);

    private static readonly Lazy<Task<DeviceOwnership>> Detection = new(() => Task.Run(Detect));

    public static Task<DeviceOwnership> DetectAsync() => Detection.Value;

    private const string OverrideVariable = "LAPSUS_FORCE_MANAGED";

    private static DeviceOwnership? Overridden() =>
        Environment.GetEnvironmentVariable(OverrideVariable)?.Trim().ToLowerInvariant() switch
        {
            "1" or "managed" or "true" => DeviceOwnership.Managed,
            "0" or "personal" or "false" => DeviceOwnership.Personal,
            _ => null
        };

    private static DeviceOwnership Detect()
    {
        try
        {
            if (Overridden() is { } forced)
                return forced;

            if (OperatingSystem.IsWindows())
                return DetectWindows();

            return OperatingSystem.IsMacOS() ? DetectMacOs() : DeviceOwnership.Unknown;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return DeviceOwnership.Unknown;
        }
    }

    [SupportedOSPlatform("windows")]
    private static DeviceOwnership DetectWindows()
    {

        if (IsDomainJoined() || IsEntraJoined() || IsMdmEnrolled())
            return DeviceOwnership.Managed;

        return DeviceOwnership.Personal;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsDomainJoined()
    {
        var buffer = IntPtr.Zero;
        try
        {
            if (NetGetJoinInformation(null, out buffer, out var status) != 0)
                return false;

            return status == NetSetupDomainName;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                NetApiBufferFree(buffer);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsEntraJoined()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
        return key?.GetSubKeyNames().Length > 0;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsMdmEnrolled()
    {
        using var enrollments = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
        if (enrollments is null)
            return false;

        foreach (var name in enrollments.GetSubKeyNames())
        {
            using var enrollment = enrollments.OpenSubKey(name);
            if (enrollment?.GetValue("EnrollmentState") is not int state || state != EnrollmentStateActive)
                continue;

            if (enrollment.GetValue("DiscoveryServiceFullURL") is string url && !string.IsNullOrWhiteSpace(url))
                return true;

            if (enrollment.GetValue("ProviderID") is "MS DM Server")
                return true;
        }

        return false;
    }

    private const int NetSetupDomainName = 3;
    private const int EnrollmentStateActive = 1;

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetJoinInformation(string? server, out IntPtr name, out int type);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [SupportedOSPlatform("macos")]
    private static DeviceOwnership DetectMacOs()
    {
        var enrollment = RunTool("/usr/bin/profiles", "status -type enrollment");
        if (enrollment is not null && enrollment.Contains(": Yes", StringComparison.OrdinalIgnoreCase))
            return DeviceOwnership.Managed;

        var directory = RunTool("/usr/sbin/dsconfigad", "-show");
        if (!string.IsNullOrWhiteSpace(directory) && directory.Contains("Active Directory Domain", StringComparison.OrdinalIgnoreCase))
            return DeviceOwnership.Managed;

        return enrollment is null ? DeviceOwnership.Unknown : DeviceOwnership.Personal;
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
            if (process.WaitForExit((int)ToolTimeout.TotalMilliseconds))
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
