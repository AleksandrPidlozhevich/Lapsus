namespace Lapsus.Core.Models;

public sealed record RuntimeCapabilities(IReadOnlyList<string> Providers)
{
    public static readonly RuntimeCapabilities CpuOnly = new([]);

    public bool Supports(string? provider)
    {
        return !string.IsNullOrEmpty(provider)
               && Providers.Any(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> ProvidersFor(ComputeDevice device)
    {
        return device == ComputeDevice.Cpu
            ? []
            : [.. Providers.Where(p => ExecutionProviders.DeviceOf(p) == device)];
    }

    public bool Has(ComputeDevice device)
    {
        return device == ComputeDevice.Cpu || ProvidersFor(device).Count > 0;
    }

    public IReadOnlyList<ComputeDevice> AvailableDevices()
    {
        return
        [
            ComputeDevice.Cpu,
            .. new[] { ComputeDevice.Gpu, ComputeDevice.Npu }.Where(Has)
        ];
    }
}
