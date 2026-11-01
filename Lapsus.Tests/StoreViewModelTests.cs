using Lapsus.Core.Licensing;
using Lapsus.Licensing.AppStore;
using Lapsus.Localization;
using Lapsus.ViewModels;

namespace Lapsus.Tests;

public sealed class StoreViewModelTests
{
    [Fact]
    public void The_website_build_gates_nothing()
    {
        var store = new StoreViewModel(new FakePurchases(), isStoreBuild: false);

        Assert.Equal(StoreAccessKind.NotStoreBuild, store.Access.Kind);
        Assert.True(store.PaidFeatures);
    }

    [Fact]
    public void The_store_build_unlocks_nothing_until_the_app_store_says_so()
    {
        var store = new StoreViewModel(new FakePurchases(), isStoreBuild: true);

        Assert.Equal(StoreAccessKind.Free, store.Access.Kind);
        Assert.False(store.PaidFeatures);
        Assert.True(store.CanSubscribe);
    }

    [Fact]
    public async Task Starting_the_trial_unlocks_and_offers_cancelling_it()
    {
        var purchases = new FakePurchases();
        var store = new StoreViewModel(purchases, isStoreBuild: true);
        await store.RefreshAsync();
        Assert.Equal(Localizer.Instance.Format("Store_YearlyTrial", "2 months", "$9.99"), store.YearlyLabel);

        purchases.Owned = new StoreEntitlement(StorePlan.Yearly, DateTimeOffset.Now.AddDays(60), IsTrial: true);
        purchases.Trial = null;
        await store.SubscribeCommand.ExecuteAsync(null);

        Assert.Equal(StoreAccessKind.Trial, store.Access.Kind);
        Assert.True(store.PaidFeatures);
        Assert.True(store.IsTrial);
        Assert.True(store.IsSubscribed);
        Assert.False(store.CanSubscribe);
        Assert.Equal(Localizer.Instance.Format("Store_Yearly", "$9.99"), store.YearlyLabel);
    }

    [Fact]
    public async Task Someone_who_had_the_trial_is_offered_the_full_price()
    {
        var store = new StoreViewModel(new FakePurchases { Trial = null }, isStoreBuild: true);

        await store.RefreshAsync();

        Assert.Null(store.YearlyTrial);
        Assert.Equal(Localizer.Instance.Format("Store_Yearly", "$9.99"), store.YearlyLabel);
    }

    [Fact]
    public async Task A_lifetime_purchase_unlocks_and_tells_the_settings()
    {
        var purchases = new FakePurchases();
        var store = new StoreViewModel(purchases, isStoreBuild: true);
        var changes = 0;
        store.AccessChanged += (_, _) => changes++;
        Assert.False(store.PaidFeatures);

        purchases.Owned = new StoreEntitlement(StorePlan.Lifetime);
        await store.BuyLifetimeCommand.ExecuteAsync(null);

        Assert.True(store.PaidFeatures);
        Assert.Equal(StoreAccessKind.Lifetime, store.Access.Kind);
        Assert.Equal(1, changes);
        Assert.False(store.CanBuyLifetime);
    }

    [Fact]
    public async Task A_pending_purchase_says_so_and_unlocks_nothing_yet()
    {
        var purchases = new FakePurchases { Result = new StorePurchaseResult(StorePurchaseStatus.Pending) };
        var store = new StoreViewModel(purchases, isStoreBuild: true);

        await store.SubscribeCommand.ExecuteAsync(null);

        Assert.Equal(Localizer.Instance["Store_Pending"], store.Message);
        Assert.False(store.PaidFeatures);
    }

    [Fact]
    public async Task Prices_come_from_the_store()
    {
        var store = new StoreViewModel(new FakePurchases(), isStoreBuild: true);

        await store.RefreshAsync();

        Assert.Equal("$9.99", store.YearlyPrice);
        Assert.Equal("$29.99", store.LifetimePrice);
        Assert.Equal("2 months", store.YearlyTrial);
        Assert.Equal(string.Empty, store.Message);
    }

    private sealed class FakePurchases : IStorePurchases
    {
        public StoreEntitlement Owned { get; set; } = StoreEntitlement.None;

        public StorePurchaseResult Result { get; set; } = new(StorePurchaseStatus.Purchased);

        public string? Trial { get; set; } = "2 months";

        public bool IsAvailable => true;

        public event EventHandler? OwnedChanged
        {
            add { }
            remove { }
        }

        public Task<StoreEntitlement> OwnedAsync() => Task.FromResult(Owned);

        public Task<IReadOnlyList<StoreProduct>> ProductsAsync(string language) =>
            Task.FromResult<IReadOnlyList<StoreProduct>>(
                [new StoreProduct(StoreProducts.Yearly, "$9.99", Trial), new StoreProduct(StoreProducts.Lifetime, "$29.99")]);

        public Task<StorePurchaseResult> PurchaseAsync(string productId) => Task.FromResult(Result);

        public Task<StoreEntitlement> RestoreAsync() => Task.FromResult(Owned);
    }
}
