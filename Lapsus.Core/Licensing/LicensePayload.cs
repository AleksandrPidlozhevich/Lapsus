using System.Text.Json.Serialization;

namespace Lapsus.Core.Licensing;

public sealed class LicensePayload
{
    public string? Name { get; set; }

    public string? Edition { get; set; }

    public int Seats { get; set; }

    public string? Issued { get; set; }

    public string? Expires { get; set; }

    public string? Machine { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LicensePayload))]
public partial class LicenseJsonContext : JsonSerializerContext;
