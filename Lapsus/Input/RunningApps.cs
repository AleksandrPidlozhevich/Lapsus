using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Input;

public sealed record RunningApp(string ProcessName, string Title);

internal static class RunningApps
{
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static IReadOnlyList<RunningApp> List()
    {
        if (OperatingSystem.IsWindows())
            return ListWindows();

        return OperatingSystem.IsMacOS() ? ListMac() : [];
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<RunningApp> ListWindows()
    {
        var self = Environment.ProcessId;
        var found = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
            using (process)
            {
                if (process.Id == self)
                    continue;

                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    var name = process.ProcessName;
                    if (string.IsNullOrWhiteSpace(name) || found.ContainsKey(name))
                        continue;

                    found[name] = new RunningApp(name, DescriptionOf(process) ?? name);
                }
                catch
                {

                }
            }

        return [.. found.Values.OrderBy(app => app.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    [SupportedOSPlatform("windows")]
    private static string? DescriptionOf(Process process)
    {
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            return string.IsNullOrWhiteSpace(description) ? null : description;
        }
        catch
        {

            return null;
        }
    }

    [SupportedOSPlatform("macos")]
    private static IReadOnlyList<RunningApp> ListMac()
    {
        if (MacOSNativeMethods.CgWindowOwnerName == IntPtr.Zero
            || MacOSNativeMethods.CgWindowOwnerPid == IntPtr.Zero
            || MacOSNativeMethods.CgWindowLayer == IntPtr.Zero)
            return [];

        var list = MacOSNativeMethods.CGWindowListCopyWindowInfo(
            MacOSNativeMethods.CgWindowListOptionOnScreenOnly
            | MacOSNativeMethods.CgWindowListExcludeDesktopElements,
            0);
        if (list == IntPtr.Zero)
            return [];

        try
        {
            var self = Environment.ProcessId;
            var found = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
            var buffer = new StringBuilder(256);
            var count = MacOSNativeMethods.CFArrayGetCount(list);

            for (long i = 0; i < count; i++)
            {
                var dict = MacOSNativeMethods.CFArrayGetValueAtIndex(list, i);
                if (dict == IntPtr.Zero)
                    continue;

                var layerValue = MacOSNativeMethods.CFDictionaryGetValue(
                    dict, MacOSNativeMethods.CgWindowLayer);
                if (layerValue == IntPtr.Zero
                    || !MacOSNativeMethods.CFNumberGetValue(
                        layerValue, MacOSNativeMethods.CfNumberIntType, out var layer)
                    || layer != 0)
                    continue;

                var pidValue = MacOSNativeMethods.CFDictionaryGetValue(
                    dict, MacOSNativeMethods.CgWindowOwnerPid);
                if (pidValue == IntPtr.Zero
                    || !MacOSNativeMethods.CFNumberGetValue(
                        pidValue, MacOSNativeMethods.CfNumberIntType, out var pid)
                    || pid == self || pid <= 0)
                    continue;

                var processName = MacProcessNames.Read(pid, buffer);
                if (string.IsNullOrWhiteSpace(processName) || found.ContainsKey(processName))
                    continue;

                var title = MacOSNativeMethods.ReadCfString(
                               MacOSNativeMethods.CFDictionaryGetValue(
                                   dict, MacOSNativeMethods.CgWindowOwnerName))
                           ?? processName;
                found[processName] = new RunningApp(processName, title);
            }

            return [.. found.Values.OrderBy(app => app.Title, StringComparer.CurrentCultureIgnoreCase)];
        }
        finally
        {
            MacOSNativeMethods.CFRelease(list);
        }
    }
}
