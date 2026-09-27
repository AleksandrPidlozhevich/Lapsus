using Lapsus.Core.Licensing;

namespace Lapsus.Core.Tests;

// Golden token from the TypeScript issuer (throwaway pair); regenerate via issue() with this pair.
public class ActivationFormatTests
{
    private const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEbcu8PESyrOKIHFPqPPRASLHBjPes7ZyJwI6JOGhzYs+dYrFuBD4iZlx9H2slB+uqDNujqIotZ9e4HfupvVKhtQ==";

    private const string TokenFromTypeScript =
        "LAPSUS-1.eyJuYW1lIjoiQ29uZm9ybWFuY2UgVmVjdG9yIiwiZWRpdGlvbiI6ImJ1c2luZXNzIiwic2VhdHMiOjMsImlzc3VlZCI6IjIwMjYtMDEtMDEiLCJleHBpcmVzIjoiMjA5OS0wMS0wMSIsIm1hY2hpbmUiOiJnb2xkZW4tdmVjdG9yLW1hY2hpbmUifQ" +
        ".whkAzR3-79W1RcmwcSFgIkQP9gfu7uI21EL2b6tOIl1DvgyPaGA7YKQvHdqjb7ulWo5pjOU8m2QhIdLK2V2QsQ";

    private static readonly DateOnly Today = new(2026, 9, 5);

    [Fact]
    public void TheAppAcceptsATokenMintedByTheServer()
    {
        var verifier = new LicenseVerifier(PublicKey);

        Assert.True(verifier.TryVerify(TokenFromTypeScript, Today, out var license, out var error));
        Assert.Equal(LicenseKeyError.None, error);
    }

    [Fact]
    public void EveryFieldSurvivesTheCrossing()
    {
        new LicenseVerifier(PublicKey).TryVerify(TokenFromTypeScript, Today, out var license, out _);

        Assert.Equal(LicenseEdition.Business, license.Edition);
        Assert.Equal("Conformance Vector", license.HolderName);
        Assert.Equal(3, license.Seats);
        Assert.Equal(new DateOnly(2026, 1, 1), license.Issued);
        Assert.Equal(new DateOnly(2099, 1, 1), license.Expires);
        Assert.Equal("golden-vector-machine", license.Machine);
    }

    [Fact]
    public void TheTokenIsUselessOnAnotherMachine()
    {
        new LicenseVerifier(PublicKey).TryVerify(TokenFromTypeScript, Today, out var license, out _);

        Assert.True(license.IsForMachine("golden-vector-machine"));
        Assert.False(license.IsForMachine("some-other-machine"));
    }
}
