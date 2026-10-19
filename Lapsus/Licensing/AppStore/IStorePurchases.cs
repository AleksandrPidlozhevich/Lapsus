using Lapsus.Core.Licensing;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lapsus.Licensing.AppStore;

public sealed record StoreProduct(string Id, string DisplayPrice);

public enum StorePurchaseStatus
{
    Purchased,
    Cancelled,
    Pending,
    Unavailable,
    Failed
}

public readonly record struct StorePurchaseResult(StorePurchaseStatus Status, string? Error = null);

// The App Store side of the Mac App Store edition. Every other build gets NullStorePurchases.
public interface IStorePurchases
{
    bool IsAvailable { get; }

    // Renewals, refunds and purchases made elsewhere while the app runs. Raised on any thread.
    event EventHandler? OwnedChanged;

    Task<StoreEntitlement> OwnedAsync();

    Task<IReadOnlyList<StoreProduct>> ProductsAsync();

    Task<StorePurchaseResult> PurchaseAsync(string productId);

    Task<StoreEntitlement> RestoreAsync();
}

internal sealed class NullStorePurchases : IStorePurchases
{
    public bool IsAvailable => false;

    public event EventHandler? OwnedChanged
    {
        add { }
        remove { }
    }

    public Task<StoreEntitlement> OwnedAsync() => Task.FromResult(StoreEntitlement.None);

    public Task<IReadOnlyList<StoreProduct>> ProductsAsync() => Task.FromResult<IReadOnlyList<StoreProduct>>([]);

    public Task<StorePurchaseResult> PurchaseAsync(string productId) =>
        Task.FromResult(new StorePurchaseResult(StorePurchaseStatus.Unavailable));

    public Task<StoreEntitlement> RestoreAsync() => Task.FromResult(StoreEntitlement.None);
}
