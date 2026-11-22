namespace Lapsus;

// The Mac App Store edition (-p:MacAppStore=true) is free and ships without the paid modules: neural
// correction and voice typing are not compiled in, and nothing is sold or licensed there.
internal static class AppEdition
{
#if MAS
    public const bool IsAppStore = true;
#else
    public const bool IsAppStore = false;
#endif

    public const bool HasPaidModules = !IsAppStore;
}
