using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lapsus.Localization;
using Lapsus.Settings;

namespace Lapsus.ViewModels;

public sealed partial class InstalledDictionary(DictionaryDescriptor descriptor, bool isOutdated) : ObservableObject
{
    public DictionaryDescriptor Descriptor { get; } = descriptor;

    public string Title => Descriptor.Title;

    public string License => Descriptor.License;

    [ObservableProperty] private bool _isOutdated = isOutdated;
}

public sealed partial class DictionariesViewModel : ObservableObject
{
    private readonly DictionaryStore _store;
    private readonly Func<Task> _rebuildBrain;

    public DictionariesViewModel(DictionaryStore store, Func<Task> rebuildBrain)
    {
        _store = store;
        _rebuildBrain = rebuildBrain;

        foreach (var descriptor in DictionaryCatalog.Available)
        {
            if (store.IsInstalled(descriptor.Code))
                Insert(Installed, new InstalledDictionary(descriptor, store.IsOutdated(descriptor)), d => d.Descriptor.DisplayName);
            else
                Insert(Available, descriptor, d => d.DisplayName);
        }

        _selectedAvailable = Available.FirstOrDefault();
        Installed.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasInstalled));
    }

    private static Localizer L => Localizer.Instance;

    private static readonly StringComparer NameOrder =
        StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);

    private static void Insert<T>(ObservableCollection<T> list, T item, Func<T, string> name)
    {
        var at = 0;
        while (at < list.Count && NameOrder.Compare(name(list[at]), name(item)) < 0)
            at++;
        list.Insert(at, item);
    }

    public ObservableCollection<InstalledDictionary> Installed { get; } = [];

    public ObservableCollection<DictionaryDescriptor> Available { get; } = [];

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(UpdateCommand))]
    private DictionaryDescriptor? _selectedAvailable;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(UpdateCommand))]
    private bool _busy;

    [ObservableProperty] private string _status = string.Empty;

    public bool HasInstalled => Installed.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        var descriptor = SelectedAvailable;
        if (descriptor is null)
            return;

        if (await DownloadAsync(descriptor, "Dict_Installed"))
        {
            Available.Remove(descriptor);
            Insert(Installed, new InstalledDictionary(descriptor, false), d => d.Descriptor.DisplayName);
            SelectedAvailable = Available.FirstOrDefault();
            await _rebuildBrain();
        }
    }

    private bool CanAdd()
    {
        return SelectedAvailable is not null && !Busy;
    }

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync(InstalledDictionary? installed)
    {
        if (installed is null)
            return;

        if (await DownloadAsync(installed.Descriptor, "Dict_Updated"))
        {
            installed.IsOutdated = false;
            await _rebuildBrain();
        }
    }

    private bool CanUpdate(InstalledDictionary? installed)
    {
        return !Busy;
    }

    private async Task<bool> DownloadAsync(DictionaryDescriptor descriptor, string doneKey)
    {
        Busy = true;
        Status = L.Format("Dict_Downloading", descriptor.DisplayName);
        try
        {
            await _store.DownloadAsync(descriptor);
            Status = L.Format(doneKey, descriptor.DisplayName);
            return true;
        }
        catch (Exception ex)
        {
            Status = L.Format("Dict_DownloadFailed", ex.Message);
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(InstalledDictionary? installed)
    {
        if (installed is null)
            return;

        var descriptor = installed.Descriptor;
        _store.Remove(descriptor.Code);
        Installed.Remove(installed);
        if (!Available.Contains(descriptor))
            Insert(Available, descriptor, d => d.DisplayName);
        SelectedAvailable ??= descriptor;
        Status = L.Format("Dict_Removed", descriptor.DisplayName);
        await _rebuildBrain();
    }
}
