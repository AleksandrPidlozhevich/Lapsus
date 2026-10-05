using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Core.Models;
using Lapsus.Localization;
using Lapsus.Neural;
using Lapsus.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Lapsus.ViewModels;

public sealed record ComputeDeviceOption(ComputeDevicePreference Preference, string Name);

public sealed partial class ModelsViewModel : ObservableObject
{
    private readonly ModelStore _modelStore;
    private readonly HuggingFaceCatalog _catalog;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly Func<Task> _rebuildBrain;
    private readonly Func<bool> _neuralEnabled;
    private readonly Action _enableNeuralForInstall;

    public ModelsViewModel(
        ModelStore modelStore, AppSettings settings, SettingsStore store,
        Func<Task> rebuildBrain, Func<bool> neuralEnabled, Action enableNeuralForInstall,
        HuggingFaceCatalog? catalog = null)
    {
        _modelStore = modelStore;
        _catalog = catalog ?? new HuggingFaceCatalog();
        _settings = settings;
        _store = store;
        _rebuildBrain = rebuildBrain;
        _neuralEnabled = neuralEnabled;
        _enableNeuralForInstall = enableNeuralForInstall;

        var installed = modelStore.Installed();
        foreach (var descriptor in installed)
            Installed.Add(descriptor);
        foreach (var descriptor in ModelCatalog.Available)
            if (installed.All(m => m.Id != descriptor.Id))
                Available.Add(descriptor);
        _selectedAvailable = Available.FirstOrDefault();
        _selectedInstalled = Installed.FirstOrDefault(m => m.Id == settings.SelectedModelId)
                             ?? Installed.FirstOrDefault();

        DeviceOptions = BuildDeviceOptions();
        _selectedDevice = DeviceOptions.FirstOrDefault(o => o.Preference == settings.ComputeDevice)
                          ?? DeviceOptions[0];

        Found.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFound));
    }

    private static Localizer L => Localizer.Instance;

    private static RuntimeCapabilities Capabilities => ComputeDeviceProbe.Capabilities();

    public ObservableCollection<ModelDescriptor> Installed { get; } = [];

    public ObservableCollection<ModelDescriptor> Available { get; } = [];

    public ObservableCollection<ModelDescriptor> Found { get; } = [];

    public bool HasFound => Found.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private ModelDescriptor? _selectedAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private ModelDescriptor? _selectedInstalled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFoundCommand))]
    private ModelDescriptor? _selectedFound;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFoundCommand))]
    private bool _busy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchQuery = string.Empty;

    [ObservableProperty] private string _status = string.Empty;

    [ObservableProperty] private double _downloadProgress;

    public IReadOnlyList<ComputeDeviceOption> DeviceOptions { get; private set; }

    [ObservableProperty] private ComputeDeviceOption? _selectedDevice;

    public string DeviceHint => Capabilities.Has(ComputeDevice.Gpu)
        ? L.Format("Model_DeviceHint", ComputeDeviceProbe.Label(
            Capabilities.ProvidersFor(ComputeDevice.Gpu).FirstOrDefault()))
        : L["Model_DeviceHintCpuOnly"];

    private static IReadOnlyList<ComputeDeviceOption> BuildDeviceOptions()
    {
        return
        [
            new(ComputeDevicePreference.Auto, L["Model_DeviceAuto"]),
            new(ComputeDevicePreference.Cpu, L["Model_DeviceCpu"]),
            .. Capabilities.AvailableDevices()
                .Where(d => d != ComputeDevice.Cpu)
                .Select(d => new ComputeDeviceOption(
                    d == ComputeDevice.Gpu ? ComputeDevicePreference.Gpu : ComputeDevicePreference.Npu,
                    L[d == ComputeDevice.Gpu ? "Model_DeviceGpu" : "Model_DeviceNpu"]))
        ];
    }

    partial void OnSelectedDeviceChanged(ComputeDeviceOption? value)
    {
        if (value is null || _settings.ComputeDevice == value.Preference)
            return;

        _settings.ComputeDevice = value.Preference;
        _store.Save(_settings);
        if (_neuralEnabled() && !Busy)
            _ = _rebuildBrain();
    }

    public void RefreshLabels()
    {
        var preference = SelectedDevice?.Preference ?? ComputeDevicePreference.Auto;
        DeviceOptions = BuildDeviceOptions();
        OnPropertyChanged(nameof(DeviceOptions));
        SelectedDevice = DeviceOptions.FirstOrDefault(o => o.Preference == preference) ?? DeviceOptions[0];
        OnPropertyChanged(nameof(DeviceHint));
    }

    [ObservableProperty]
    private bool _isModelLoaded;

    public void ReportLoading(string displayName)
    {
        IsModelLoaded = false;
        Status = L.Format("Model_Loading", displayName);
    }

    public void ReportLoadFailed(string message)
    {
        IsModelLoaded = false;
        Status = L.Format("Model_LoadFailed", message);
    }

    public void ReportNeedsModel()
    {
        IsModelLoaded = false;
        Status = L["Model_NeedsDownload"];
    }

    public void ReportReady(string displayName, string executionProvider = "")
    {
        IsModelLoaded = true;
        Status = string.IsNullOrEmpty(executionProvider)
            ? L.Format("Model_Ready", displayName)
            : L.Format("Model_ReadyOn", displayName, executionProvider);
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddAsync()
    {
        var descriptor = SelectedAvailable;
        return descriptor is null ? Task.CompletedTask : InstallAsync(descriptor);
    }

    private bool CanAdd()
    {
        return SelectedAvailable is not null && !Busy;
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        Busy = true;
        Status = L["Model_Searching"];
        try
        {
            Found.Clear();

            if (LooksLikeReference(query) && HuggingFaceModelRef.TryParse(query, out var reference))
            {
                var descriptor = await _catalog.DescribeAsync(reference);
                if (descriptor is not null)
                    Found.Add(descriptor);
            }
            else
            {
                foreach (var descriptor in await _catalog.SearchAsync(query))
                    Found.Add(descriptor);
            }

            SelectedFound = Found.FirstOrDefault();
            Status = Found.Count == 0
                ? L["Model_SearchEmpty"]
                : L.Format("Model_SearchFound", Found.Count);
        }
        catch (Exception ex)
        {
            Status = L.Format("Model_SearchFailed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    private bool CanSearch()
    {
        return !Busy && !string.IsNullOrWhiteSpace(SearchQuery);
    }

    private static bool LooksLikeReference(string query)
    {
        return query.Contains('/', StringComparison.Ordinal) && !query.Contains(' ', StringComparison.Ordinal);
    }

    [RelayCommand(CanExecute = nameof(CanAddFound))]
    private Task AddFoundAsync()
    {
        var descriptor = SelectedFound;
        return descriptor is null ? Task.CompletedTask : InstallAsync(descriptor);
    }

    private bool CanAddFound()
    {
        return SelectedFound is not null && !Busy;
    }

    private async Task InstallAsync(ModelDescriptor descriptor)
    {
        var variant = descriptor.PickVariant(_settings.ComputeDevice, Capabilities);
        if (variant is null)
        {
            Status = L["Model_DownloadEmpty"];
            return;
        }

        Busy = true;
        DownloadProgress = 0;
        Status = L.Format("Model_Downloading", descriptor.DisplayName, 0);
        try
        {

            if (_modelStore.IsInstalled(descriptor.Id)
                || string.Equals(_settings.SelectedModelId, descriptor.Id, StringComparison.Ordinal))
            {
                var previousId = _settings.SelectedModelId;
                _settings.SelectedModelId = null;
                _store.Save(_settings);
                await _rebuildBrain();
                _settings.SelectedModelId = previousId;
                _store.Save(_settings);
            }

            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                Status = L.Format("Model_Downloading", descriptor.DisplayName, (int)(p * 100));
            });
            await _modelStore.DownloadAsync(descriptor, variant, progress);

            var installed = _modelStore.Describe(descriptor.Id) ?? descriptor;
            Available.Remove(descriptor);
            Found.Remove(descriptor);
            var existing = Installed.FirstOrDefault(m => m.Id == installed.Id);
            if (existing is not null)
                Installed.Remove(existing);
            Installed.Add(installed);
            SelectedAvailable = Available.FirstOrDefault();
            SelectedFound = Found.FirstOrDefault();
            SelectedInstalled = installed;

            _settings.SelectedModelId = installed.Id;
            _store.Save(_settings);
            _enableNeuralForInstall();
            Status = L.Format("Model_Installed", installed.DisplayName);
            await _rebuildBrain();
        }
        catch (Exception ex)
        {
            Status = L.Format("Model_DownloadFailed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        var descriptor = SelectedInstalled;
        if (descriptor is null)
            return;

        Busy = true;
        var previousId = _settings.SelectedModelId;
        try
        {
            Installed.Remove(descriptor);
            var catalog = ModelCatalog.Find(descriptor.Id);
            if (catalog is not null && Available.All(m => m.Id != catalog.Id))
                Available.Add(catalog);
            SelectedAvailable ??= Available.FirstOrDefault();
            SelectedInstalled = Installed.FirstOrDefault();
            _settings.SelectedModelId = SelectedInstalled?.Id;
            _store.Save(_settings);

            await _rebuildBrain();
            _modelStore.Remove(descriptor.Id);

            Status = L.Format("Model_Removed", descriptor.DisplayName);
        }
        catch (Exception ex)
        {

            RestoreRemoved(descriptor, previousId);
            Status = L.Format("Model_RemoveFailed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    private void RestoreRemoved(ModelDescriptor descriptor, string? previousId)
    {
        if (Installed.All(m => m.Id != descriptor.Id))
            Installed.Add(descriptor);

        var returned = Available.FirstOrDefault(m => m.Id == descriptor.Id);
        if (returned is not null)
        {
            Available.Remove(returned);
            if (ReferenceEquals(SelectedAvailable, returned))
                SelectedAvailable = Available.FirstOrDefault();
        }

        _settings.SelectedModelId = previousId;
        _store.Save(_settings);
        SelectedInstalled = Installed.FirstOrDefault(m => m.Id == previousId) ?? Installed.FirstOrDefault();
    }

    private bool CanRemove()
    {
        return SelectedInstalled is not null && !Busy;
    }

    partial void OnSelectedInstalledChanged(ModelDescriptor? value)
    {
        if (value is null || _settings.SelectedModelId == value.Id)
            return;

        _settings.SelectedModelId = value.Id;
        _store.Save(_settings);
        if (_neuralEnabled() && !Busy)
            _ = _rebuildBrain();
    }
}
