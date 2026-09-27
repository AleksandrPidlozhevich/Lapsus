using System;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;

namespace Lapsus.Input;

internal static class InputBackendFactory
{
    public static IInputBackend CreateForCurrentPlatform(
        IPhraseCorrector corrector, AppExclusions? excludedApps = null)
    {
        if (OperatingSystem.IsWindows())
            return new WindowsInputBackend(corrector, excludedApps);
        if (OperatingSystem.IsMacOS())
            return new MacOSInputBackend(corrector, excludedApps);
        return new NullInputBackend();
    }

    public static bool IsSupported(IInputBackend backend)
    {
        return backend is not NullInputBackend;
    }
}
