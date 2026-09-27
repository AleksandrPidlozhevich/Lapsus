namespace Lapsus.Core.Models;

public sealed record ModelVariant(
    string SubPath,
    ComputeDevice Device,
    string Provider = "",
    int SizeMb = 0)
{
    private static readonly string[] UndeclaredGpuProviders =
        [ExecutionProviders.DirectMl, ExecutionProviders.Cuda];

    public bool IsUsable(RuntimeCapabilities capabilities)
    {
        return Device == ComputeDevice.Cpu || ResolveProvider(capabilities) is not null;
    }

    public string? ResolveProvider(RuntimeCapabilities capabilities)
    {
        if (Device == ComputeDevice.Cpu)
            return null;
        if (Provider.Length > 0)
            return capabilities.Supports(Provider) ? ExecutionProviders.Normalize(Provider) : null;
        if (Device == ComputeDevice.Gpu)
            return UndeclaredGpuProviders.FirstOrDefault(capabilities.Supports);
        return capabilities.ProvidersFor(Device).FirstOrDefault();
    }
}
