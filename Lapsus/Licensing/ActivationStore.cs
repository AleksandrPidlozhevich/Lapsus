using Lapsus.Core.Licensing;
using System;
using System.IO;

namespace Lapsus.Licensing;

public sealed class ActivationStore
{
    private readonly string _path;
    private readonly LicenseVerifier _verifier;
    private readonly Func<string> _fingerprint;

    public ActivationStore(LicenseVerifier? verifier = null, Func<string>? fingerprint = null)
    {
        _verifier = verifier ?? LicenseVerifier.Default;
        _fingerprint = fingerprint ?? (() => MachineFingerprint.Current);

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus");
        _path = Path.Combine(dir, "activation.key");

        Reload();
    }

    public License Current { get; private set; } = License.Free;

    public LicenseKeyError Error { get; private set; } = LicenseKeyError.Empty;

    public bool IsActivated => Current.Edition != LicenseEdition.Free;

    public bool NeedsRenewal(DateOnly today, int daysBefore = 10)
    {
        if (!IsActivated || Current.Expires is not { } expires)
            return false;

        return expires.DayNumber - today.DayNumber <= daysBefore;
    }

    public bool Install(string? token)
    {
        var trimmed = token?.Trim() ?? string.Empty;
        if (!Verify(trimmed, out var license, out var error))
        {
            Error = error;
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, trimmed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {

        }

        Current = license;
        Error = LicenseKeyError.None;
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

        Current = License.Free;
        Error = LicenseKeyError.Empty;
    }

    public void Reload()
    {
        var text = ReadFile();
        if (text.Length == 0)
        {
            Current = License.Free;
            Error = LicenseKeyError.Empty;
            return;
        }

        Current = Verify(text, out var license, out var error) ? license : License.Free;
        Error = error;
    }

    private bool Verify(string token, out License license, out LicenseKeyError error)
    {
        license = License.Free;

        if (token.Length == 0)
        {
            error = LicenseKeyError.Empty;
            return false;
        }

        if (!_verifier.TryVerify(token, Today, out var parsed, out error))
            return false;

        if (!parsed.IsForMachine(_fingerprint()))
        {
            error = LicenseKeyError.WrongMachine;
            return false;
        }

        license = parsed;
        error = LicenseKeyError.None;
        return true;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private string ReadFile()
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
