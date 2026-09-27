using System.Buffers.Text;
using System.Text;
using Lapsus.Core.Licensing;

namespace Lapsus.Core.Tests;

public class LicenseKeyTests
{
    private static readonly DateOnly Today = new(2026, 9, 4);

    private readonly (string PublicKey, string PrivateKey) _keys = LicenseIssuer.CreateKeyPair();

    [Fact]
    public void AcceptsAKeyItJustIssued()
    {
        var key = Issue(new LicensePayload
        {
            Name = "Anna Kowalska",
            Edition = "pro",
            Seats = 1,
            Issued = "2026-09-01"
        });

        Assert.True(Verifier().TryVerify(key, Today, out var license, out var error));
        Assert.Equal(LicenseKeyError.None, error);
        Assert.Equal(LicenseEdition.Pro, license.Edition);
        Assert.Equal("Anna Kowalska", license.HolderName);
        Assert.Equal(1, license.Seats);
        Assert.Null(license.Expires);
    }

    [Fact]
    public void ReadsSeatsAndExpiryOfABusinessKey()
    {
        var key = Issue(new LicensePayload
        {
            Name = "Acme sp. z o.o.",
            Edition = "business",
            Seats = 25,
            Issued = "2026-09-01",
            Expires = "2027-09-01"
        });

        Assert.True(Verifier().TryVerify(key, Today, out var license, out _));
        Assert.Equal(LicenseEdition.Business, license.Edition);
        Assert.Equal(25, license.Seats);
        Assert.Equal(new DateOnly(2027, 9, 1), license.Expires);
    }

    [Fact]
    public void CarriesTheMachineOfAnActivationToken()
    {
        var token = Issue(new LicensePayload
        {
            Name = "Acme",
            Edition = "business",
            Seats = 25,
            Issued = "2026-09-01",
            Expires = "2026-10-06",
            Machine = "m4c41nEf1n63rpr1nt"
        });

        Assert.True(Verifier().TryVerify(token, Today, out var license, out _));
        Assert.True(license.IsMachineBound);
        Assert.True(license.IsForMachine("m4c41nEf1n63rpr1nt"));
    }

    [Fact]
    public void ATokenIssuedForAnotherMachineIsNotForThisOne()
    {
        var token = Issue(new LicensePayload
        {
            Edition = "business",
            Seats = 25,
            Issued = "2026-09-01",
            Machine = "colleagues-laptop"
        });

        Assert.True(Verifier().TryVerify(token, Today, out var license, out var error));

        Assert.Equal(LicenseKeyError.None, error);
        Assert.False(license.IsForMachine("my-laptop"));
    }

    [Fact]
    public void APlainKeyIsBoundToNoMachine()
    {
        var key = Issue(new LicensePayload { Edition = "pro", Seats = 1, Issued = "2026-09-01" });

        Assert.True(Verifier().TryVerify(key, Today, out var license, out _));
        Assert.False(license.IsMachineBound);
        Assert.False(license.IsForMachine(""));
        Assert.False(license.IsForMachine("any-machine"));
    }

    [Fact]
    public void SurvivesTheWhitespaceThatPastingFromEmailAdds()
    {
        var key = Issue(new LicensePayload { Edition = "pro", Seats = 1, Issued = "2026-09-01" });
        var wrapped = $"  {key[..20]}\r\n{key[20..]}  ";

        Assert.True(Verifier().TryVerify(wrapped, Today, out _, out var error));
        Assert.Equal(LicenseKeyError.None, error);
    }

    [Fact]
    public void RejectsAnEditedPayload()
    {
        var key = Issue(new LicensePayload
        {
            Name = "Anna",
            Edition = "pro",
            Seats = 1,
            Issued = "2026-09-01"
        });

        var body = key[LicenseVerifier.Prefix.Length..];
        var dot = body.IndexOf('.');
        var payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(body.AsSpan(0, dot)));
        var edited = payload.Replace("\"pro\"", "\"business\"");
        Assert.NotEqual(payload, edited);

        var tampered =
            $"{LicenseVerifier.Prefix}{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(edited))}.{body[(dot + 1)..]}";

        Assert.False(Verifier().TryVerify(tampered, Today, out var license, out var error));
        Assert.Equal(LicenseKeyError.BadSignature, error);
        Assert.Equal(LicenseEdition.Free, license.Edition);
    }

    [Fact]
    public void RejectsAKeySignedByAnotherPair()
    {
        var stranger = LicenseIssuer.CreateKeyPair();
        var key = LicenseIssuer.Issue(
            new LicensePayload { Edition = "business", Seats = 100, Issued = "2026-09-01" },
            stranger.PrivateKey);

        Assert.False(Verifier().TryVerify(key, Today, out _, out var error));
        Assert.Equal(LicenseKeyError.BadSignature, error);
    }

    [Fact]
    public void ReportsAnExpiredKeyAsExpiredButStillReadsIt()
    {
        var key = Issue(new LicensePayload
        {
            Name = "Acme",
            Edition = "business",
            Seats = 5,
            Issued = "2025-01-01",
            Expires = "2026-01-01"
        });

        Assert.False(Verifier().TryVerify(key, Today, out var license, out var error));
        Assert.Equal(LicenseKeyError.Expired, error);

        Assert.Equal(new DateOnly(2026, 1, 1), license.Expires);
        Assert.Equal("Acme", license.HolderName);
    }

    [Fact]
    public void AcceptsAKeyOnItsLastDay()
    {
        var key = Issue(new LicensePayload
        {
            Edition = "business",
            Seats = 5,
            Issued = "2025-09-04",
            Expires = "2026-09-04"
        });

        Assert.True(Verifier().TryVerify(key, Today, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TreatsNothingEnteredAsEmpty(string? input)
    {
        Assert.False(Verifier().TryVerify(input, Today, out _, out var error));
        Assert.Equal(LicenseKeyError.Empty, error);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("LAPSUS-1.")]
    [InlineData("LAPSUS-1.abc")]
    [InlineData("LAPSUS-1.abc.")]
    [InlineData("LAPSUS-1..abc")]
    [InlineData("LAPSUS-1.!!!.!!!")]
    [InlineData("LAPSUS-2.abc.def")]
    public void RejectsGarbageWithoutThrowing(string input)
    {
        Assert.False(Verifier().TryVerify(input, Today, out _, out var error));
        Assert.Equal(LicenseKeyError.Malformed, error);
    }

    [Fact]
    public void RejectsAnEditionThisBuildDoesNotKnow()
    {
        var key = Issue(new LicensePayload { Edition = "enterprise", Seats = 1, Issued = "2026-09-01" });

        Assert.False(Verifier().TryVerify(key, Today, out _, out var error));
        Assert.Equal(LicenseKeyError.Malformed, error);
    }

    [Fact]
    public void RejectsAnUnreadableIssueDate()
    {
        var key = Issue(new LicensePayload { Edition = "pro", Seats = 1, Issued = "not a date" });

        Assert.False(Verifier().TryVerify(key, Today, out _, out var error));
        Assert.Equal(LicenseKeyError.Malformed, error);
    }

    [Fact]
    public void AnUnconfiguredBuildVerifiesNothing()
    {
        var verifier = new LicenseVerifier("");
        var key = Issue(new LicensePayload { Edition = "business", Seats = 5, Issued = "2026-09-01" });

        Assert.False(verifier.IsConfigured);
        Assert.False(verifier.TryVerify(key, Today, out _, out var error));
        Assert.Equal(LicenseKeyError.NotConfigured, error);
    }

    [Fact]
    public void ATypoInTheEmbeddedKeyLeavesTheVerifierUnconfiguredRatherThanThrowing()
    {
        var verifier = new LicenseVerifier("this is not base64 at all");

        Assert.False(verifier.IsConfigured);
        Assert.False(verifier.TryVerify("anything", Today, out _, out var error));
        Assert.Equal(LicenseKeyError.NotConfigured, error);
    }

    [Fact]
    public void TheShippedVerifierNeverThrows()
    {
        var exception = Record.Exception(() => LicenseVerifier.Default.TryVerify("x", Today, out _, out _));

        Assert.Null(exception);
    }

    private LicenseVerifier Verifier() => new(_keys.PublicKey);

    private string Issue(LicensePayload payload) => LicenseIssuer.Issue(payload, _keys.PrivateKey);
}
