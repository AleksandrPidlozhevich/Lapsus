using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Licensing;

public enum ActivationError
{
    None,

    RejectedKey,

    SeatsExhausted,

    Unreachable,

    NoFingerprint
}

public readonly record struct ActivationResult(string? Token, ActivationError Error)
{
    public bool Ok => Error == ActivationError.None && !string.IsNullOrEmpty(Token);
}

// Only licensing network call: key + fingerprint → token. Do not add other outbound calls.
public sealed class ActivationClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly string _endpoint;

    public ActivationClient(string? endpoint = null, HttpClient? http = null)
    {
        _endpoint = endpoint ?? LicenseLinks.ActivationEndpoint;
        _http = http ?? new HttpClient { Timeout = Timeout };
    }

    public async Task<ActivationResult> ActivateAsync(
        string key, string machine, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(machine))
            return new ActivationResult(null, ActivationError.NoFingerprint);

        try
        {
            using var response = await _http
                .PostAsJsonAsync(_endpoint, new ActivationRequest(key, machine),
                    ActivationJsonContext.Default.ActivationRequest, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content
                    .ReadFromJsonAsync(ActivationJsonContext.Default.ActivationResponse, cancellationToken)
                    .ConfigureAwait(false);

                return string.IsNullOrEmpty(body?.Token)
                    ? new ActivationResult(null, ActivationError.Unreachable)
                    : new ActivationResult(body.Token, ActivationError.None);
            }

            if ((int)response.StatusCode >= 500)
                return new ActivationResult(null, ActivationError.Unreachable);

            var failure = await response.Content
                .ReadFromJsonAsync(ActivationJsonContext.Default.ActivationResponse, cancellationToken)
                .ConfigureAwait(false);

            return new ActivationResult(null, failure?.Error switch
            {
                "seats_exhausted" => ActivationError.SeatsExhausted,
                "invalid_key" => ActivationError.RejectedKey,
                _ => ActivationError.Unreachable
            });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new ActivationResult(null, ActivationError.Unreachable);
        }
    }
}

public sealed record ActivationRequest(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("machine")] string Machine);

public sealed record ActivationResponse(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("error")] string? Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ActivationRequest))]
[JsonSerializable(typeof(ActivationResponse))]
internal partial class ActivationJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
