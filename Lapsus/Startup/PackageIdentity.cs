using System;
using System.Runtime.InteropServices;

namespace Lapsus.Startup;

// MSIX/Store: Store updates it (no Velopack self-update); HKCU Run is swallowed by the package hive.
internal static class PackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;

    public static bool IsPackaged { get; } = OperatingSystem.IsWindows() && HasPackageIdentity();

    private static bool HasPackageIdentity()
    {
        try
        {

            uint length = 0;
            return GetCurrentPackageFullName(ref length, null) == ErrorInsufficientBuffer;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}
