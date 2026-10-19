using System;

namespace Lapsus.Startup;

// Mac App Store builds run in the App Sandbox: no Accessibility API, no child processes, no files
// outside the container. The Mac counterpart of PackageIdentity.IsPackaged.
internal static class MacSandbox
{
    // macOS sets this for every sandboxed process and for nothing else.
    private const string ContainerVariable = "APP_SANDBOX_CONTAINER_ID";

    public static bool IsSandboxed { get; } =
        Detect(OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable(ContainerVariable));

    internal static bool Detect(bool isMacOS, string? containerId)
    {
        return isMacOS && !string.IsNullOrEmpty(containerId);
    }
}
