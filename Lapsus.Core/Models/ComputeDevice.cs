namespace Lapsus.Core.Models;

public enum ComputeDevice
{
    Cpu,
    Gpu,

    Npu
}

public enum ComputeDevicePreference
{
    Auto,
    Cpu,
    Gpu,
    Npu
}

public static class ExecutionProviders
{
    public const string DirectMl = "dml";
    public const string Cuda = "cuda";
    public const string WebGpu = "webgpu";
    public const string Qnn = "qnn";

    private static readonly Dictionary<string, ComputeDevice> Devices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu"] = ComputeDevice.Cpu,
        [DirectMl] = ComputeDevice.Gpu,
        ["directml"] = ComputeDevice.Gpu,
        [Cuda] = ComputeDevice.Gpu,
        ["rocm"] = ComputeDevice.Gpu,
        [WebGpu] = ComputeDevice.Gpu,
        ["nvtensorrtrtx"] = ComputeDevice.Gpu,
        [Qnn] = ComputeDevice.Npu,
        ["vitisai"] = ComputeDevice.Npu,
        ["ryzenai"] = ComputeDevice.Npu
    };

    public static ComputeDevice DeviceOf(string? provider)
    {
        return provider is not null && Devices.TryGetValue(provider, out var device) ? device : ComputeDevice.Cpu;
    }

    public static string Normalize(string provider)
    {
        return string.Equals(provider, "directml", StringComparison.OrdinalIgnoreCase)
            ? DirectMl
            : provider.ToLowerInvariant();
    }
}
