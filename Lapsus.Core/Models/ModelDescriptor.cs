namespace Lapsus.Core.Models;

public sealed record ModelDescriptor(
    string Id,
    string DisplayName,
    string HuggingFaceRepo,
    IReadOnlyList<ModelVariant> Variants,
    string License,
    int ApproxRamMb = 0)
{
    public int RamMb => ApproxRamMb > 0
        ? ApproxRamMb
        : Variants.Where(v => v.SizeMb > 0).Select(v => v.SizeMb).DefaultIfEmpty(0).Min();

    public string Title => RamMb > 0 ? $"{DisplayName} (~{RamMb} MB)" : DisplayName;

    public IReadOnlyList<ComputeDevice> Devices =>
        [.. Variants.Select(v => v.Device).Distinct()];

    public ModelVariant? PickVariant(ComputeDevicePreference preference, RuntimeCapabilities capabilities)
    {
        foreach (var device in Wishlist(preference))
        {
            var match = Variants.FirstOrDefault(v => v.Device == device && v.IsUsable(capabilities));
            if (match is not null)
                return match;
        }

        return Variants.FirstOrDefault(v => v.Device == ComputeDevice.Cpu) ?? Variants.FirstOrDefault();
    }

    private static IEnumerable<ComputeDevice> Wishlist(ComputeDevicePreference preference)
    {
        return preference switch
        {
            ComputeDevicePreference.Cpu => [ComputeDevice.Cpu],
            ComputeDevicePreference.Gpu => [ComputeDevice.Gpu, ComputeDevice.Cpu],
            ComputeDevicePreference.Npu => [ComputeDevice.Npu, ComputeDevice.Cpu],
            _ => [ComputeDevice.Gpu, ComputeDevice.Npu, ComputeDevice.Cpu]
        };
    }
}
