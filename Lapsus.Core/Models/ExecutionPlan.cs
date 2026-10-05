namespace Lapsus.Core.Models;

public readonly record struct ExecutionAttempt(string? Provider, ComputeDevice Device)
{
    public static ExecutionAttempt Cpu => new(null, ComputeDevice.Cpu);
}

public static class ExecutionPlan
{
    public static IReadOnlyList<ExecutionAttempt> Build(
        ComputeDevicePreference preference,
        RuntimeCapabilities capabilities,
        ComputeDevice packDevice,
        IReadOnlyList<string>? declaredProviders = null)
    {
        var declared = declaredProviders ?? [];
        var plan = new List<ExecutionAttempt>();

        foreach (var device in Wishlist(preference))
            foreach (var provider in ProvidersFor(device, capabilities, packDevice, declared))
                if (!plan.Any(a => string.Equals(a.Provider, provider, StringComparison.OrdinalIgnoreCase)))
                    plan.Add(new ExecutionAttempt(provider, device));

        plan.Add(ExecutionAttempt.Cpu);
        return plan;
    }

    private static IEnumerable<string> ProvidersFor(
        ComputeDevice device,
        RuntimeCapabilities capabilities,
        ComputeDevice packDevice,
        IReadOnlyList<string> declared)
    {
        if (device == ComputeDevice.Cpu)
            yield break;

        var asked = declared.Where(p => ExecutionProviders.DeviceOf(p) == device).ToList();
        if (asked.Count > 0)
        {
            foreach (var provider in asked.Where(capabilities.Supports))
                yield return ExecutionProviders.Normalize(provider);
            yield break;
        }

        if (packDevice == device)
            foreach (var provider in capabilities.ProvidersFor(device))
                yield return provider;
    }

    private static IEnumerable<ComputeDevice> Wishlist(ComputeDevicePreference preference)
    {
        return preference switch
        {
            ComputeDevicePreference.Cpu => [],
            ComputeDevicePreference.Gpu => [ComputeDevice.Gpu],
            ComputeDevicePreference.Npu => [ComputeDevice.Npu],
            _ => [ComputeDevice.Gpu, ComputeDevice.Npu]
        };
    }
}
