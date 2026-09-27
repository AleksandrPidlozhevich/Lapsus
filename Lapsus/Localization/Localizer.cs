using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;

namespace Lapsus.Localization;

public sealed record LanguageOption(string Code, string NativeName)
{
    public string Abbreviation => Code.ToUpperInvariant();
}

public sealed class Localizer
{
    public static Localizer Instance { get; } = new();

    private readonly ResourceManager _resources =
        new("Lapsus.Localization.Strings", typeof(Localizer).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    private Localizer()
    {
    }

    public event EventHandler? LanguageChanged;

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("en", "English"),
        new("ar", "العربية"),
        new("be", "Беларуская"),
        new("bg", "Български"),
        new("cs", "Čeština"),
        new("da", "Dansk"),
        new("de", "Deutsch"),
        new("el", "Ελληνικά"),
        new("es", "Español"),
        new("fr", "Français"),
        new("he", "עברית"),
        new("hu", "Magyar"),
        new("it", "Italiano"),
        new("ka", "ქართული"),
        new("mk", "Македонски"),
        new("nl", "Nederlands"),
        new("pl", "Polski"),
        new("pt", "Português"),
        new("ro", "Română"),
        new("ru", "Русский"),
        new("sk", "Slovenčina"),
        new("sl", "Slovenščina"),
        new("sr", "Srpski"),
        new("sv", "Svenska"),
        new("tr", "Türkçe"),
        new("uk", "Українська")
    ];

    public string CurrentCode => _culture.TwoLetterISOLanguageName;

    public bool IsRightToLeft => _culture.TextInfo.IsRightToLeft;

    public string this[string key] => _resources.GetString(key, _culture) ?? key;

    public string Format(string key, params object?[] args)
    {
        return string.Format(_culture, this[key], args);
    }

    public void SetLanguage(string code)
    {
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return;
        }

        if (culture.Equals(_culture))
            return;

        _culture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
