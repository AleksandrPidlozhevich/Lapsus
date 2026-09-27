using System;
using System.IO;
using Lapsus.Core.Licensing;

namespace Lapsus.Licensing;

public sealed class LicenseStore
{
    private readonly string _path;
    private readonly LicenseVerifier _verifier;

    public LicenseStore(LicenseVerifier? verifier = null)
    {
        _verifier = verifier ?? LicenseVerifier.Default;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus");
        _path = Path.Combine(dir, "license.key");

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

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, key!.Trim());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {

        }

        KeyText = key!.Trim();
        Current = license;
        Error = LicenseKeyError.None;
        ExpiredOn = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Remove()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

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

    private string ReadKeyFile()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path).Trim() : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
