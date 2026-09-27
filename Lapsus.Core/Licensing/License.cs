namespace Lapsus.Core.Licensing;

public enum LicenseEdition
{
    Free,
    Pro,
    Business
}

public sealed record License(
    LicenseEdition Edition,
    string HolderName,
    int Seats,
    DateOnly Issued,
    DateOnly? Expires,
    string? Machine = null)
{
    public static License Free { get; } =
        new(LicenseEdition.Free, string.Empty, 0, default, null);

    public bool IsMachineBound => !string.IsNullOrEmpty(Machine);

    public bool IsExpired(DateOnly today) => Expires is { } expires && today > expires;

    public bool IsForMachine(string fingerprint) =>
        IsMachineBound && string.Equals(Machine, fingerprint, StringComparison.Ordinal);
}

public enum LicenseKeyError
{
    None,
    Empty,

    Malformed,

    BadSignature,

    Expired,

    WrongMachine,

    NotConfigured
}
