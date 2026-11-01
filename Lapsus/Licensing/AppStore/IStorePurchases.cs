using Lapsus.Core.Licensing;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lapsus.Licensing.AppStore;

// FreeTrial: the length of the free trial, e.g. "2 months", when this user can still start it.
public sealed record StoreProduct(string Id, string DisplayPrice, string? FreeTrial = null);

public enum StorePurchaseStatus
{
    Purchased,
    Cancelled,
    Pending,
    Unavailable,
    Failed
}

public readonly record struct StorePurchaseResult(StorePurchaseStatus Status, string? Error = null);

public interface IStorePurchases
{
    bool IsAvailable { get; }

    // Renewals, refunds and purchases made elsewhere while the app runs. Raised on any thread.
    event EventHandler? OwnedChanged;

    Task<StoreEntitlement> OwnedAsync();

    Task<IReadOnlyList<StoreProduct>> ProductsAsync(string language);

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

    public Task<IReadOnlyList<StoreProduct>> ProductsAsync(string language) => Task.FromResult<IReadOnlyList<StoreProduct>>([]);

    public Task<StorePurchaseResult> PurchaseAsync(string productId) =>
        Task.FromResult(new StorePurchaseResult(StorePurchaseStatus.Unavailable));

    public Task<StoreEntitlement> RestoreAsync() => Task.FromResult(StoreEntitlement.None);
}
