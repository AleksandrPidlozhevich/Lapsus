using System.Text.Json.Serialization;

namespace Lapsus.Core.Models;

public sealed record ModelManifest(
    string Id,
    string DisplayName,
    string HuggingFaceRepo,
    string RepoSubPath,
    int ApproxRamMb,
    string License,
    bool Custom)
{
    [JsonConverter(typeof(JsonStringEnumConverter<ComputeDevice>))]
    public ComputeDevice Device { get; init; } = ComputeDevice.Cpu;

    public string Provider { get; init; } = "";

    public ModelDescriptor ToDescriptor()
    {
        return new ModelDescriptor(Id, DisplayName, HuggingFaceRepo,
            [new ModelVariant(RepoSubPath, Device, Provider, ApproxRamMb)],
            License, ApproxRamMb);
    }

    public ModelVariant ToVariant()
    {
        return new ModelVariant(RepoSubPath, Device, Provider, ApproxRamMb);
    }
}
