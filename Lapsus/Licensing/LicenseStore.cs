using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Lapsus.Core.Licensing;

namespace Lapsus.Licensing;

public sealed class LicenseStore
{
    private static readonly byte[] Entropy = "Lapsus licence key"u8.ToArray();

    private readonly string _directory;
    private readonly string _plainPath;
    private readonly string _sealedPath;
    private readonly LicenseVerifier _verifier;

    public LicenseStore(LicenseVerifier? verifier = null, string? directory = null)
    {
        _verifier = verifier ?? LicenseVerifier.Default;
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus");
        _plainPath = Path.Combine(_directory, "license.key");
        _sealedPath = Path.Combine(_directory, "license.dat");

        Reload();
    }

    public License Current { get; private set; } = License.Free;

    public LicenseKeyError Error { get; private set; } = LicenseKeyError.Empty;

    public string KeyText { get; private set; } = string.Empty;

    public DateOnly? ExpiredOn { get; private set; }

    public bool IsLicensed => Current.Edition != LicenseEdition.Free;

    public event EventHandler? Changed;

    public bool Install(string? key, out LicenseKeyError error)
    {
        if (!_verifier.TryVerify(key, Today, out var license, out error))
            return false;

        var text = key!.Trim();
        TryPersist(text);

        KeyText = text;
        Current = license;
        Error = LicenseKeyError.None;
        ExpiredOn = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Remove()
    {
        DeleteQuietly(_sealedPath);
        DeleteQuietly(_plainPath);

        KeyText = string.Empty;
        Current = License.Free;
        Error = LicenseKeyError.Empty;
        ExpiredOn = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reload()
    {
        KeyText = ReadKeyFile();
        if (string.IsNullOrEmpty(KeyText))
        {
            Current = License.Free;
            Error = LicenseKeyError.Empty;
            return;
        }

        _verifier.TryVerify(KeyText, Today, out var license, out var error);
        Error = error;
        Current = error == LicenseKeyError.None ? license : License.Free;

        ExpiredOn = error == LicenseKeyError.Expired ? license.Expires : null;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // Windows seals the key to the current user with DPAPI. Other systems keep the plain file,
    // which already lives in the per-user app-data directory.
    private void TryPersist(string key)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            if (OperatingSystem.IsWindows())
                WriteSealed(key);
            else
                WritePlain(key);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
        }
    }

    private string ReadKeyFile()
    {
        try
        {
            if (OperatingSystem.IsWindows() && TryUnseal() is { Length: > 0 } sealedKey)
            {
                // The plain file is the pre-DPAPI copy, or a leftover from a failed delete.
                DeleteQuietly(_plainPath);
                return sealedKey;
            }

            var plain = ReadPlain();
            if (plain.Length > 0 && OperatingSystem.IsWindows())
                TryPersist(plain);

            return plain;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private string ReadPlain()
    {
        return File.Exists(_plainPath) ? File.ReadAllText(_plainPath).Trim() : string.Empty;
    }

    [SupportedOSPlatform("windows")]
    private void WriteSealed(string key)
    {
        // A crash mid-write must not destroy the previous blob. Replace it only once the new
        // bytes are complete, and drop the plain copy only after that replace has landed.
        var temp = _sealedPath + ".tmp";
        try
        {
            File.WriteAllBytes(temp, ProtectedData.Protect(
                Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser));
            File.Move(temp, _sealedPath, overwrite: true);
        }
        finally
        {
            DeleteQuietly(temp);
        }

        DeleteQuietly(_plainPath);
    }

    private void WritePlain(string key)
    {
        File.WriteAllText(_plainPath, key);
        DeleteQuietly(_sealedPath);
    }

    // Null when the blob is missing or will not open (truncated write, different user).
    // The caller then still has the plain file, if an earlier build left one.
    [SupportedOSPlatform("windows")]
    private string? TryUnseal()
    {
        if (!File.Exists(_sealedPath))
            return null;

        try
        {
            var data = ProtectedData.Unprotect(
                File.ReadAllBytes(_sealedPath), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
