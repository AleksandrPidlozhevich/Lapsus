using Lapsus.Core.Licensing;
using Lapsus.Licensing.AppStore;
using Lapsus.Localization;
using Lapsus.Settings;
using Lapsus.ViewModels;

namespace Lapsus.Tests;

public sealed class StoreViewModelTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"lapsus-store-{Guid.NewGuid():N}.json");

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }

    [Fact]
    public void The_website_build_gates_nothing_and_starts_no_trial()
    {
        var settings = new AppSettings();

        var store = new StoreViewModel(new FakePurchases(), settings, new SettingsStore(_settingsPath), isStoreBuild: false);

        Assert.Equal(StoreAccessKind.NotStoreBuild, store.Access.Kind);
        Assert.True(store.PaidFeatures);
        Assert.Null(settings.StoreTrialStartedOn);
        Assert.False(File.Exists(_settingsPath));
    }

    [Fact]
    public void The_first_store_launch_starts_the_trial_and_remembers_the_day()
    {
        var settings = new AppSettings();

        var store = new StoreViewModel(new FakePurchases(), settings, new SettingsStore(_settingsPath), isStoreBuild: true);

        Assert.Equal(StoreAccessKind.Trial, store.Access.Kind);
        Assert.Equal(Today, settings.StoreTrialStartedOn);
        Assert.Equal(Today, new SettingsStore(_settingsPath).Load().StoreTrialStartedOn);
    }

    [Fact]
    public async Task A_purchase_after_the_trial_unlocks_and_tells_the_settings()
    {
        var purchases = new FakePurchases();
        var settings = new AppSettings { StoreTrialStartedOn = Today.AddDays(-StoreAccessPolicy.TrialDays) };
        var store = new StoreViewModel(purchases, settings, new SettingsStore(_settingsPath), isStoreBuild: true);
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
        var settings = new AppSettings { StoreTrialStartedOn = Today.AddDays(-StoreAccessPolicy.TrialDays) };
        var store = new StoreViewModel(purchases, settings, new SettingsStore(_settingsPath), isStoreBuild: true);

        await store.SubscribeCommand.ExecuteAsync(null);

        Assert.Equal(Localizer.Instance["Store_Pending"], store.Message);
        Assert.False(store.PaidFeatures);
    }

    [Fact]
    public async Task Prices_come_from_the_store()
    {
        var store = new StoreViewModel(new FakePurchases(), new AppSettings(), new SettingsStore(_settingsPath), isStoreBuild: true);

        await store.RefreshAsync();

        Assert.Equal("$9.99", store.YearlyPrice);
        Assert.Equal("$29.99", store.LifetimePrice);
        Assert.Equal(string.Empty, store.Message);
    }

    private sealed class FakePurchases : IStorePurchases
    {
        public StoreEntitlement Owned { get; set; } = StoreEntitlement.None;

        public StorePurchaseResult Result { get; set; } = new(StorePurchaseStatus.Purchased);

        public bool IsAvailable => true;

        public event EventHandler? OwnedChanged
        {
            add { }
            remove { }
        }

        public Task<StoreEntitlement> OwnedAsync() => Task.FromResult(Owned);

        public Task<IReadOnlyList<StoreProduct>> ProductsAsync() =>
            Task.FromResult<IReadOnlyList<StoreProduct>>(
                [new StoreProduct(StoreProducts.Yearly, "$9.99"), new StoreProduct(StoreProducts.Lifetime, "$29.99")]);

        public Task<StorePurchaseResult> PurchaseAsync(string productId) => Task.FromResult(Result);

        public Task<StoreEntitlement> RestoreAsync() => Task.FromResult(Owned);
    }
}
