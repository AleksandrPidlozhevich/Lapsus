using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lapsus.Core.Licensing;

public sealed class LicenseVerifier
{
    public const string Prefix = "LAPSUS-1.";

    // Do not change: issued keys verify against this ECDSA P-256 SPKI.
    private const string EmbeddedPublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEtrNyPoyZ/4bUYmEOnV82/WeyMq5YCj1TP6sw1cPYlAwQD0napN8RrAT3bVAKs9YjiahqHehJQGIaXEi1JURxdg==";

    private readonly byte[]? _publicKey;

    public LicenseVerifier(string publicKeySpkiBase64)
    {
        _publicKey = TryDecodePublicKey(publicKeySpkiBase64);
    }

    public static LicenseVerifier Default { get; } = new(EmbeddedPublicKey);

    public bool IsConfigured => _publicKey is not null;

    public bool TryVerify(
        string? key,
        DateOnly today,
        out License license,
        out LicenseKeyError error)
    {
        license = License.Free;

        if (_publicKey is null)
        {
            error = LicenseKeyError.NotConfigured;
            return false;
        }

        var trimmed = Compact(key);
        if (trimmed.Length == 0)
        {
            error = LicenseKeyError.Empty;
            return false;
        }

        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = LicenseKeyError.Malformed;
            return false;
        }

        var body = trimmed.AsSpan(Prefix.Length);
        var separator = body.IndexOf('.');
        if (separator <= 0 || separator == body.Length - 1)
        {
            error = LicenseKeyError.Malformed;
            return false;
        }

        if (!TryDecode(body[..separator], out var payload) ||
            !TryDecode(body[(separator + 1)..], out var signature))
        {
            error = LicenseKeyError.Malformed;
            return false;
        }

        if (!VerifySignature(payload, signature))
        {
            error = LicenseKeyError.BadSignature;
            return false;
        }

        // Verify the original payload bytes, never a re-serialized object.
        if (!TryReadPayload(payload, out var parsed))
        {
            error = LicenseKeyError.Malformed;
            return false;
        }

        license = parsed;
        if (parsed.IsExpired(today))
        {
            error = LicenseKeyError.Expired;
            return false;
        }

        error = LicenseKeyError.None;
        return true;
    }

    private static byte[]? TryDecodePublicKey(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return null;

        try
        {
            var key = Convert.FromBase64String(Compact(base64));
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key, out _);
            return key;
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private bool VerifySignature(byte[] payload, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(_publicKey, out _);
            return ecdsa.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryReadPayload(byte[] payload, out License license)
    {
        license = License.Free;

        try
        {
            var body = JsonSerializer.Deserialize(payload, LicenseJsonContext.Default.LicensePayload);
            if (body is null || !TryParseEdition(body.Edition, out var edition))
                return false;

            if (!DateOnly.TryParse(body.Issued, out var issued))
                return false;

            DateOnly? expires = null;
            if (!string.IsNullOrEmpty(body.Expires))
            {
                if (!DateOnly.TryParse(body.Expires, out var parsed))
                    return false;

                expires = parsed;
            }

            license = new License(
                edition, body.Name ?? string.Empty, Math.Max(body.Seats, 0), issued, expires, body.Machine);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseEdition(string? value, out LicenseEdition edition)
    {
        return Enum.TryParse(value, ignoreCase: true, out edition) && Enum.IsDefined(edition);
    }

    private static bool TryDecode(ReadOnlySpan<char> text, out byte[] bytes)
    {
        try
        {
            bytes = Base64Url.DecodeFromChars(text);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static string Compact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
            if (!char.IsWhiteSpace(c))
                builder.Append(c);

        return builder.ToString();
    }
}
