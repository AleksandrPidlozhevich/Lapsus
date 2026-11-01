using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Core.Licensing;
using Lapsus.Licensing;
using Lapsus.Licensing.AppStore;
using Lapsus.Localization;
using Lapsus.Startup;
using System;
using System.Threading.Tasks;

namespace Lapsus.ViewModels;

// Only neural correction and features added after it are paid; everything else stays free.
// In every other build Access gates nothing.
public sealed partial class StoreViewModel : ObservableObject
{
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromHours(1);

    private readonly IStorePurchases _purchases;
    private readonly DispatcherTimer? _recheck;

    private StoreEntitlement _owned = StoreEntitlement.None;

    public StoreViewModel(IStorePurchases purchases)
        : this(purchases, MacSandbox.IsSandboxed)
    {
    }

    internal StoreViewModel(IStorePurchases purchases, bool isStoreBuild)
    {
        _purchases = purchases;
        IsStoreBuild = isStoreBuild;
        _access = Decide();

        if (!IsStoreBuild)
            return;

        _purchases.OwnedChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());
        Localizer.Instance.LanguageChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            RaiseTextChanged();
            // The trial length is spelled out by the App Store side in the language it was asked for.
            _ = LoadProductsAsync();
        });

        // An expiry is not always announced as a transaction update, so entitlements are re-read.
        _recheck = new DispatcherTimer { Interval = RecheckInterval };
        _recheck.Tick += (_, _) => _ = RefreshAsync();
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

    // Raised on the UI thread.
    public event EventHandler? AccessChanged;

    public bool PaidFeatures => Access.PaidFeatures;

    [ObservableProperty] private string? _yearlyPrice;

    [ObservableProperty] private string? _lifetimePrice;

    [ObservableProperty] private string? _yearlyTrial;

    [ObservableProperty] private string _message = string.Empty;

    [ObservableProperty] private bool _busy;

    public string StatusTitle => Access.Kind switch
    {
        StoreAccessKind.Trial => L.Format("Store_Title_Trial", Access.TrialDaysLeft),
        StoreAccessKind.Subscription => L["Store_Title_Subscription"],
        StoreAccessKind.Lifetime => L["Store_Title_Lifetime"],
        _ => L["Store_Title_Free"]
    };

    public string StatusDetail => Access switch
    {
        { Kind: StoreAccessKind.Trial, Renews: { } ends } => L.Format("Store_TrialDetail", ends.ToLocalTime().ToString("d")),
        { Kind: StoreAccessKind.Subscription, Renews: { } renews } => L.Format("Store_Renews", renews.ToLocalTime().ToString("d")),
        _ => L["Store_Detail"]
    };

    // Lifetime is still offered to a subscriber: it replaces the subscription for good.
    public bool CanSubscribe => Access.Kind == StoreAccessKind.Free;

    public bool CanBuyLifetime => Access.Kind is not StoreAccessKind.Lifetime;

    // The trial is already the subscription, so it is cancelled in the same place.
    public bool IsSubscribed => Access.Kind is StoreAccessKind.Subscription or StoreAccessKind.Trial;

    public bool IsTrial => Access.Kind == StoreAccessKind.Trial;

    public bool PricesKnown => YearlyPrice is not null || LifetimePrice is not null;

    public bool ShowSubscribe => CanSubscribe && YearlyPrice is not null;

    public bool ShowBuyLifetime => CanBuyLifetime && LifetimePrice is not null;

    public string YearlyLabel => YearlyTrial is { } trial
        ? L.Format("Store_YearlyTrial", trial, YearlyPrice ?? "…")
        : L.Format("Store_Yearly", YearlyPrice ?? "…");

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

        if (!PricesKnown)
            await LoadProductsAsync().ConfigureAwait(true);
    }

    private async Task LoadProductsAsync()
    {
        try
        {
            foreach (var product in await _purchases.ProductsAsync(L.CurrentCode).ConfigureAwait(true))
            {
                if (product.Id == StoreProducts.Yearly)
                {
                    YearlyPrice = product.DisplayPrice;
                    YearlyTrial = product.FreeTrial;
                }
                else if (product.Id == StoreProducts.Lifetime)
                {
                    LifetimePrice = product.DisplayPrice;
                }
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
            Message = L.Format("Store_RestoreFailed", ex.Message);
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
            {
                await RefreshAsync().ConfigureAwait(true);
                // Starting the trial uses it up; a later subscription is offered at full price.
                await LoadProductsAsync().ConfigureAwait(true);
            }
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

    private StoreAccess Decide() => StoreAccessPolicy.Decide(IsStoreBuild, _owned, DateTimeOffset.Now);

    partial void OnYearlyPriceChanged(string? value) => RaiseTextChanged();

    partial void OnLifetimePriceChanged(string? value) => RaiseTextChanged();

    partial void OnYearlyTrialChanged(string? value) => RaiseTextChanged();

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
