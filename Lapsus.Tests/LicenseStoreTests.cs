using Lapsus.Core.Licensing;
using Lapsus.Core.Tests;
using Lapsus.Licensing;
using System.Text;

namespace Lapsus.Tests;

public sealed class LicenseStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lapsus-license-" + Guid.NewGuid().ToString("N"));
    private readonly LicenseVerifier _verifier;
    private readonly string _key;

    public LicenseStoreTests()
    {
        var (publicKey, privateKey) = LicenseIssuer.CreateKeyPair();
        _verifier = new LicenseVerifier(publicKey);
        _key = LicenseIssuer.Issue(new LicensePayload
        {
            Name = "Anna Kowalska",
            Edition = "pro",
            Seats = 1,
            Issued = "2026-09-01"
        }, privateKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void An_installed_key_survives_a_restart()
    {
        Assert.True(Open().Install(_key, out _));

        var reopened = Open();

        Assert.True(reopened.IsLicensed);
        Assert.Equal(_key, reopened.KeyText);
    }

    [Fact]
    public void The_key_never_lands_on_disk_in_plain_text_on_windows()
    {
        Assert.True(Open().Install(_key, out _));

        var file = Assert.Single(Directory.GetFiles(_dir));
        var bytes = File.ReadAllBytes(file);
        var keyBytes = Encoding.UTF8.GetBytes(_key);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Path.Combine(_dir, "license.dat"), file);
            Assert.Equal(-1, bytes.AsSpan().IndexOf(keyBytes));
        }
        else
        {
            Assert.Equal(Path.Combine(_dir, "license.key"), file);
            Assert.Equal(_key, Encoding.UTF8.GetString(bytes));
        }
    }

    [Fact]
    public void A_plain_key_from_an_earlier_build_is_sealed_and_removed_on_windows()
    {
        var plainPath = PlantPlainKey();

        var store = Open();

        Assert.True(store.IsLicensed);
        Assert.Equal(_key, store.KeyText);
        Assert.Equal(!OperatingSystem.IsWindows(), File.Exists(plainPath));
        Assert.True(Open().IsLicensed);
    }

    [Fact]
    public void A_sealed_file_that_will_not_open_falls_back_to_the_plain_key()
    {
        PlantPlainKey();
        File.WriteAllBytes(Path.Combine(_dir, "license.dat"), "not a sealed key"u8.ToArray());

        var store = Open();

        Assert.True(store.IsLicensed);
        Assert.Equal(_key, store.KeyText);
        Assert.Equal(!OperatingSystem.IsWindows(), File.Exists(Path.Combine(_dir, "license.key")));
        Assert.True(Open().IsLicensed);
    }

    [Fact]
    public void Removing_the_licence_deletes_every_copy()
    {
        var store = Open();
        Assert.True(store.Install(_key, out _));

        store.Remove();

        Assert.Empty(Directory.GetFiles(_dir));
        Assert.False(Open().IsLicensed);
    }

    private LicenseStore Open() => new(_verifier, _dir);

    private string PlantPlainKey()
    {
        Directory.CreateDirectory(_dir);
        var plainPath = Path.Combine(_dir, "license.key");
        File.WriteAllText(plainPath, _key);
        return plainPath;
    }
}
