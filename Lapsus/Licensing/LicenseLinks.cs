using System.Collections.Generic;
using Lapsus.Localization;

namespace Lapsus.Licensing;

public static class LicenseLinks
{
    public const string Website = "https://getlapsus.com";

    public const string SupportEmail = "info@getlapsus.com";

    public const string Repo = "https://github.com/AleksandrPidlozhevich/Lapsus";

    public const string Donate = "https://ko-fi.com/aliaksandrpidlazhevich";

    public static string Home => Page("");

    public static string BuyPro => Page("pricing");

    public static string BuyBusiness => Page("pricing");

    public const string Terms = $"{Repo}/blob/master/COMMERCIAL-USE.md";

    public const string LicenseText = $"{Repo}/blob/master/LICENSE";

    public const string ThirdPartyNotices = $"{Repo}/blob/master/THIRD-PARTY-NOTICES.md";

    public const string ActivationEndpoint =
        "https://ndrzaavemqplizezzvwn.supabase.co/functions/v1/lapsus-activate";

    public static string Seats => Page("pricing");

    private static readonly HashSet<string> SiteLocales =
    [
        "ar", "be", "bg", "de", "el", "en", "fr", "he", "ka", "ru", "sr", "uk"
    ];

    public static string Page(string relativePath, string? language = null)
    {
        var locale = (language ?? Localizer.Instance.CurrentCode).ToLowerInvariant();
        if (!SiteLocales.Contains(locale))
            locale = "en";

        var trimmed = relativePath.Trim('/');
        return trimmed.Length == 0
            ? $"{Website}/{locale}/"
            : $"{Website}/{locale}/{trimmed}/";
    }
}
