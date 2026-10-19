using Lapsus.Input;
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
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

    // The same IOPlatformUUID `ioreg -rd1 -c IOPlatformExpertDevice` prints, read in process: the
    // App Sandbox does not let the store build start ioreg, and the value must match what earlier
    // versions sent so existing activations stay valid.
    [SupportedOSPlatform("macos")]
    private static string? ReadMacPlatformUuid()
    {
        var service = IOServiceGetMatchingService(0, IOServiceMatching("IOPlatformExpertDevice"));
        if (service == 0)
            return null;

        var key = MacOSNativeMethods.CFStringCreateWithCString(
            IntPtr.Zero, "IOPlatformUUID", MacOSNativeMethods.CFStringEncodingUtf8);
        try
        {
            var value = IORegistryEntryCreateCFProperty(service, key, IntPtr.Zero, 0);
            if (value == IntPtr.Zero)
                return null;

            try
            {
                return MacOSNativeMethods.ReadCfString(value);
            }
            finally
            {
                MacOSNativeMethods.CFRelease(value);
            }
        }
        finally
        {
            if (key != IntPtr.Zero)
                MacOSNativeMethods.CFRelease(key);
            IOObjectRelease(service);
        }
    }

    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";

    // The matching dictionary is consumed by IOServiceGetMatchingService; it is not released here.
    [DllImport(IOKit)]
    private static extern IntPtr IOServiceMatching(string name);

    [DllImport(IOKit)]
    private static extern uint IOServiceGetMatchingService(uint mainPort, IntPtr matching);

    [DllImport(IOKit)]
    private static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

    [DllImport(IOKit)]
    private static extern int IOObjectRelease(uint obj);
}
