using System.Text.Json;

namespace Lapsus.Core.Models;

public static class GenAiPack
{
    public const string ConfigFile = "genai_config.json";

    public static IReadOnlyList<string> ReadProviders(string configJson)
    {
        var found = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("model", out var model)
                && model.TryGetProperty("decoder", out var decoder)
                && decoder.TryGetProperty("session_options", out var sessionOptions)
                && sessionOptions.TryGetProperty("provider_options", out var providers)
                && providers.ValueKind == JsonValueKind.Array)
                foreach (var entry in providers.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                        continue;
                    foreach (var property in entry.EnumerateObject())
                        found.Add(ExecutionProviders.Normalize(property.Name));
                }
        }
        catch (JsonException)
        {
        }

        return found;
    }

    public const string ClosedThinkingBlock = "<think>\n\n</think>\n\n";

    public static bool UsesThinkingTemplate(string? templateText)
    {
        return templateText is not null && templateText.Contains("enable_thinking", StringComparison.Ordinal);
    }

    public static ComputeDevice InferDevice(string subPath, IEnumerable<string>? declaredProviders = null)
    {
        var declared = declaredProviders?
            .Select(ExecutionProviders.DeviceOf)
            .FirstOrDefault(d => d != ComputeDevice.Cpu);
        if (declared is { } device)
            return device;

        if (InferProvider(subPath) is { Length: > 0 } named)
            return ExecutionProviders.DeviceOf(named);

        return subPath.Contains("gpu", StringComparison.OrdinalIgnoreCase)
            ? ComputeDevice.Gpu
            : ComputeDevice.Cpu;
    }

    public static string InferProvider(string subPath)
    {
        var path = subPath.ToLowerInvariant();
        if (path.Contains(ExecutionProviders.WebGpu, StringComparison.Ordinal))
            return ExecutionProviders.WebGpu;
        if (path.Contains("dml", StringComparison.Ordinal)
            || path.Contains("directml", StringComparison.Ordinal))
            return ExecutionProviders.DirectMl;
        if (path.Contains(ExecutionProviders.Cuda, StringComparison.Ordinal))
            return ExecutionProviders.Cuda;
        if (path.Contains(ExecutionProviders.Qnn, StringComparison.Ordinal))
            return ExecutionProviders.Qnn;
        return "";
    }
}
