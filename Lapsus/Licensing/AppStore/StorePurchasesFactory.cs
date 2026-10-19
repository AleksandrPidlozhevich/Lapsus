using Lapsus.Startup;
using System;

namespace Lapsus.Licensing.AppStore;

internal static class StorePurchasesFactory
{
    public static IStorePurchases CreateForCurrentBuild()
    {
        if (OperatingSystem.IsMacOS() && MacSandbox.IsSandboxed && MacStoreKitPurchases.TryCreate() is { } storeKit)
            return storeKit;

        return new NullStorePurchases();
    }
}
