using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Lapsus.Core.Licensing;

namespace Lapsus.Core.Tests;

internal static class LicenseIssuer
{
    public static (string PublicKey, string PrivateKey) CreateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (
            Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));
    }

    public static string Issue(LicensePayload payload, string privateKeyPkcs8Base64)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, LicenseJsonContext.Default.LicensePayload);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyPkcs8Base64), out _);
        var signature = ecdsa.SignData(
            bytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{LicenseVerifier.Prefix}{Base64Url.EncodeToString(bytes)}.{Base64Url.EncodeToString(signature)}";
    }
}
