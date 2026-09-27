using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Core.Licensing;
using Lapsus.Licensing;
using Lapsus.Localization;

namespace Lapsus.ViewModels;

public sealed partial class LicenseViewModel : ObservableObject
{
    private readonly LicenseStore _store;
    private readonly LicenseStatus _status;
    private readonly Action _openLicenseSettings;
    private readonly Action _closeReminder;

    public LicenseViewModel(LicenseStore store, LicenseStatus status,
        Action openLicenseSettings, Action closeReminder)
    {
        _store = store;
        _status = status;
        _openLicenseSettings = openLicenseSettings;
        _closeReminder = closeReminder;
        _keyInput = store.KeyText;

        Localizer.Instance.LanguageChanged += (_, _) => Dispatcher.UIThread.Post(RefreshText);
    }

    public Localizer L => Localizer.Instance;

    public FlowDirection FlowDirection =>
        Localizer.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public LicenseEdition Edition => _store.Current.Edition;

    public bool HasLicense => _status.IsLicensed;

    [ObservableProperty] private string _keyInput;

    [ObservableProperty] private string _errorText = string.Empty;

    public bool ShowLicenseNotice => _status.LicenseRequired;

    public string ReminderHeadline => _status.IsEvaluating
        ? L.Format("LicRem_TrialHeadline", _status.DaysLeft)
        : L["LicRem_DueHeadline"];

    public string ReminderBody => L["LicRem_Body"];

    public string StatusTitle => _store.Current.Edition switch
    {
        LicenseEdition.Pro => L.Format("Lic_Status_Pro", HolderOrAnonymous),
        LicenseEdition.Business => L.Format("Lic_Status_Business", HolderOrAnonymous, _store.Current.Seats),
        _ => L["Lic_Free_Title"]
    };

    public string StatusDetail
    {
        get
        {
            if (!HasLicense)
                return _status.LicenseRequired ? L["Lic_Free_Managed"] : L["Lic_Free_Hint"];

            return _store.Current.Expires is { } expires
                ? L.Format("Lic_Expires", expires.ToString("d"))
                : L["Lic_NoExpiry"];
        }
    }

    private string HolderOrAnonymous =>
        string.IsNullOrWhiteSpace(_store.Current.HolderName) ? "—" : _store.Current.HolderName;

    public void OnStatusRefreshed() => RefreshText();

    [ObservableProperty] private bool _busy;

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!_store.Install(KeyInput, out var error))
        {
            ErrorText = Describe(error);
            RefreshText();
            return;
        }

        Busy = true;
        try
        {
            var failure = await _status.ActivateAsync();
            ErrorText = Describe(failure);
        }
        finally
        {
            Busy = false;
        }

        RefreshText();

        if (ErrorText.Length == 0 && HasLicense)
            _closeReminder();
    }

    [RelayCommand]
    private void Remove()
    {
        _status.RemoveLicense();
        KeyInput = string.Empty;
        ErrorText = string.Empty;
        RefreshText();
    }

    [RelayCommand]
    private void OpenLicenseSettings()
    {
        _openLicenseSettings();
        _closeReminder();
    }

    [RelayCommand]
    private void DismissReminder() => _closeReminder();

    [RelayCommand]
    private void BuyPro() => OpenLink(LicenseLinks.BuyPro);

    [RelayCommand]
    private void BuyBusiness() => OpenLink(LicenseLinks.BuyBusiness);

    [RelayCommand]
    private void ReadTerms() => OpenLink(LicenseLinks.Terms);

    private string Describe(LicenseKeyError error) => error switch
    {
        LicenseKeyError.Empty => string.Empty,
        LicenseKeyError.BadSignature => L["Lic_Err_Signature"],
        LicenseKeyError.Expired => L.Format("Lic_Err_Expired", _store.ExpiredOn?.ToString("d") ?? "—"),
        LicenseKeyError.NotConfigured => L["Lic_Err_NotConfigured"],
        _ => L["Lic_Err_Malformed"]
    };

    private string Describe(ActivationError error) => error switch
    {
        ActivationError.None => string.Empty,
        ActivationError.SeatsExhausted => L.Format("Lic_Err_Seats", LicenseLinks.SupportEmail),
        ActivationError.RejectedKey => L["Lic_Err_Signature"],
        ActivationError.NoFingerprint => L["Lic_Err_NoMachine"],
        _ => L["Lic_Err_Offline"]
    };

    private void RefreshText()
    {
        OnPropertyChanged(nameof(Edition));
        OnPropertyChanged(nameof(HasLicense));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(ShowLicenseNotice));
        OnPropertyChanged(nameof(ReminderHeadline));
        OnPropertyChanged(nameof(ReminderBody));
    }

    internal static void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
