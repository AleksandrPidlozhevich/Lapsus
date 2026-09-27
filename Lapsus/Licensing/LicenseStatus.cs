using System;
using System.Threading.Tasks;
using Lapsus.Settings;

namespace Lapsus.Licensing;

// License check is a reminder, never a feature gate.
public sealed class LicenseStatus
{
    public const int EvaluationDays = 30;

    private readonly LicenseStore _licenses;
    private readonly ActivationStore _activations;
    private readonly ActivationClient _activation;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;

    public LicenseStatus(
        LicenseStore licenses, ActivationStore activations, ActivationClient activation,
        AppSettings settings, SettingsStore store)
    {
        _licenses = licenses;
        _activations = activations;
        _activation = activation;
        _settings = settings;
        _store = store;
    }

    public DeviceOwnership Ownership { get; private set; } = DeviceOwnership.Unknown;

    public bool HasKey => _licenses.IsLicensed;

    public bool IsLicensed => HasKey && _activations.IsActivated;

    public ActivationError LastActivationError { get; private set; }

    public bool LicenseRequired => Ownership == DeviceOwnership.Managed && !IsLicensed;

    public int DaysLeft
    {
        get
        {
            if (_settings.OrganizationDetectedOn is not { } since)
                return EvaluationDays;

            return Math.Max(0, EvaluationDays - (Today.DayNumber - since.DayNumber));
        }
    }

    public bool IsEvaluating => DaysLeft > 0;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public async Task<bool> RefreshAsync()
    {
        Ownership = await ManagedDeviceProbe.DetectAsync().ConfigureAwait(true);

        if (Ownership == DeviceOwnership.Managed && _settings.OrganizationDetectedOn is null)
        {
            _settings.OrganizationDetectedOn = Today;
            _store.Save(_settings);
        }

        await RenewIfDueAsync().ConfigureAwait(true);
        return LicenseRequired;
    }

    public async Task<ActivationError> ActivateAsync()
    {
        if (!HasKey)
            return LastActivationError = ActivationError.RejectedKey;

        var result = await _activation
            .ActivateAsync(_licenses.KeyText, MachineFingerprint.Current)
            .ConfigureAwait(true);

        if (result.Ok && _activations.Install(result.Token))
            return LastActivationError = ActivationError.None;

        return LastActivationError = result.Ok ? ActivationError.Unreachable : result.Error;
    }

    public void RemoveLicense()
    {
        _licenses.Remove();
        _activations.Remove();
        LastActivationError = ActivationError.None;
    }

    private async Task RenewIfDueAsync()
    {
        if (!HasKey)
            return;

        _activations.Reload();
        if (_activations.IsActivated && !_activations.NeedsRenewal(Today))
            return;

        await ActivateAsync().ConfigureAwait(true);
    }
}
