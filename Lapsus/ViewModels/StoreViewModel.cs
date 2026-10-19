using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Core.Licensing;
using Lapsus.Licensing;
using Lapsus.Licensing.AppStore;
using Lapsus.Localization;
using Lapsus.Settings;
using Lapsus.Startup;
using System;
using System.Threading.Tasks;

namespace Lapsus.ViewModels;

// The License tab of the Mac App Store edition: trial, subscription, lifetime purchase. Only
// neural correction and features added after it are paid there; everything else stays free. In
// every other build IsStoreBuild is false, Access gates nothing and none of this is shown.
public sealed partial class StoreViewModel : ObservableObject
{
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromHours(1);

    private readonly IStorePurchases _purchases;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly DispatcherTimer? _recheck;

    private StoreEntitlement _owned = StoreEntitlement.None;

    public StoreViewModel(IStorePurchases purchases, AppSettings settings, SettingsStore store)
        : this(purchases, settings, store, MacSandbox.IsSandboxed)
    {
    }

    internal StoreViewModel(IStorePurchases purchases, AppSettings settings, SettingsStore store, bool isStoreBuild)
    {
        _purchases = purchases;
        _settings = settings;
        _store = store;
        IsStoreBuild = isStoreBuild;

        if (IsStoreBuild && _settings.StoreTrialStartedOn is null)
        {
            _settings.StoreTrialStartedOn = Today;
            _store.Save(_settings);
        }

        _access = Decide();

        if (!IsStoreBuild)
            return;

        _purchases.OwnedChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());
        Localizer.Instance.LanguageChanged += (_, _) => Dispatcher.UIThread.Post(RaiseTextChanged);

        // The trial ends on a calendar day, not on an App Store event.
        _recheck = new DispatcherTimer { Interval = RecheckInterval };
        _recheck.Tick += (_, _) => Access = Decide();
        _recheck.Start();
    }

    public Localizer L => Localizer.Instance;

    public bool IsStoreBuild { get; }

    private StoreAccess _access;

    public StoreAccess Access
    {
        get => _access;
        private set
        {
            if (_access == value)
                return;

            _access = value;
            RaiseTextChanged();
            AccessChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Raised on the UI thread when what is paid for becomes available or stops being available.
    public event EventHandler? AccessChanged;

    public bool PaidFeatures => Access.PaidFeatures;

    [ObservableProperty] private string? _yearlyPrice;

    [ObservableProperty] private string? _lifetimePrice;

    [ObservableProperty] private string _message = string.Empty;

    [ObservableProperty] private bool _busy;

    public string StatusTitle => Access.Kind switch
    {
        StoreAccessKind.Trial => L.Format("Store_Title_Trial", Access.TrialDaysLeft),
        StoreAccessKind.Subscription => L["Store_Title_Subscription"],
        StoreAccessKind.Lifetime => L["Store_Title_Lifetime"],
        _ => L["Store_Title_Free"]
    };

    public string StatusDetail => Access is { Kind: StoreAccessKind.Subscription, Renews: { } renews }
        ? L.Format("Store_Renews", renews.ToLocalTime().ToString("d"))
        : L["Store_Detail"];

    // Lifetime is still offered to a subscriber: it replaces the subscription for good.
    public bool CanSubscribe => Access.Kind is StoreAccessKind.Trial or StoreAccessKind.Free;

    public bool CanBuyLifetime => Access.Kind is not StoreAccessKind.Lifetime;

    public bool IsSubscribed => Access.Kind == StoreAccessKind.Subscription;

    public bool IsTrial => Access.Kind == StoreAccessKind.Trial;

    public bool PricesKnown => YearlyPrice is not null || LifetimePrice is not null;

    // A buy button without a price would be a button that cannot work; until the App Store answers
    // there is a retry instead.
    public bool ShowSubscribe => CanSubscribe && YearlyPrice is not null;

    public bool ShowBuyLifetime => CanBuyLifetime && LifetimePrice is not null;

    public string YearlyLabel => L.Format("Store_Yearly", YearlyPrice ?? "…");

    public string LifetimeLabel => L.Format("Store_Lifetime", LifetimePrice ?? "…");

    public async Task RefreshAsync()
    {
        if (!IsStoreBuild)
            return;

        try
        {
            _owned = await _purchases.OwnedAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // StoreKit answers from its own cache offline; this is a broken bridge, not a network blip.
            CrashLog.Log(ex, "App Store entitlements");
        }

        Access = Decide();

        if (PricesKnown)
            return;

        try
        {
            foreach (var product in await _purchases.ProductsAsync().ConfigureAwait(true))
            {
                if (product.Id == StoreProducts.Yearly)
                    YearlyPrice = product.DisplayPrice;
                else if (product.Id == StoreProducts.Lifetime)
                    LifetimePrice = product.DisplayPrice;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Log(ex, "App Store products");
        }

        Message = PricesKnown ? string.Empty : L["Store_Unavailable"];
    }

    [RelayCommand]
    private Task SubscribeAsync() => BuyAsync(StoreProducts.Yearly);

    [RelayCommand]
    private Task BuyLifetimeAsync() => BuyAsync(StoreProducts.Lifetime);

    [RelayCommand]
    private async Task RestoreAsync()
    {
        Busy = true;
        Message = string.Empty;
        try
        {
            _owned = await _purchases.RestoreAsync().ConfigureAwait(true);
            Access = Decide();
        }
        catch (Exception ex)
        {
            Message = L.Format("Store_Failed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        Busy = true;
        try
        {
            await RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void ManageSubscription() => LicenseViewModel.OpenLink(LicenseLinks.ManageSubscriptions);

    [RelayCommand]
    private void OpenTerms() => LicenseViewModel.OpenLink(LicenseLinks.Terms);

    [RelayCommand]
    private void OpenPrivacy() => LicenseViewModel.OpenLink(LicenseLinks.Privacy);

    private async Task BuyAsync(string productId)
    {
        Busy = true;
        Message = string.Empty;
        try
        {
            var result = await _purchases.PurchaseAsync(productId).ConfigureAwait(true);
            Message = result.Status switch
            {
                StorePurchaseStatus.Purchased or StorePurchaseStatus.Cancelled => string.Empty,
                StorePurchaseStatus.Pending => L["Store_Pending"],
                StorePurchaseStatus.Unavailable => L["Store_Unavailable"],
                _ => L.Format("Store_Failed", result.Error ?? "—")
            };

            if (result.Status == StorePurchaseStatus.Purchased)
                await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Message = L.Format("Store_Failed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    private StoreAccess Decide() =>
        StoreAccessPolicy.Decide(IsStoreBuild, _owned, _settings.StoreTrialStartedOn, Today);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    partial void OnYearlyPriceChanged(string? value) => RaiseTextChanged();

    partial void OnLifetimePriceChanged(string? value) => RaiseTextChanged();

    private void RaiseTextChanged()
    {
        OnPropertyChanged(nameof(Access));
        OnPropertyChanged(nameof(PaidFeatures));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(CanSubscribe));
        OnPropertyChanged(nameof(CanBuyLifetime));
        OnPropertyChanged(nameof(IsSubscribed));
        OnPropertyChanged(nameof(IsTrial));
        OnPropertyChanged(nameof(PricesKnown));
        OnPropertyChanged(nameof(ShowSubscribe));
        OnPropertyChanged(nameof(ShowBuyLifetime));
        OnPropertyChanged(nameof(YearlyLabel));
        OnPropertyChanged(nameof(LifetimeLabel));
    }
}
