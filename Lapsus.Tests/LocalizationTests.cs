using System.Collections;
using System.Globalization;
using System.Resources;
using Lapsus.Localization;

namespace Lapsus.Tests;

public class LocalizationTests
{
    private static readonly ResourceManager Resources =
        new("Lapsus.Localization.Strings", typeof(Localizer).Assembly);

    private static HashSet<string> NeutralKeys => KeysOf(CultureInfo.InvariantCulture, tryParents: true)!;

    public static TheoryData<string> TranslatedLanguages()
    {
        var data = new TheoryData<string>();
        foreach (var language in Localizer.Instance.Languages)
            if (language.Code != "en")
                data.Add(language.Code);

        return data;
    }

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void Every_offered_language_ships_its_own_resources(string code)
    {
        Assert.NotNull(KeysOf(CultureInfo.GetCultureInfo(code), tryParents: false));
    }

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void Every_offered_language_is_key_complete(string code)
    {
        var keys = KeysOf(CultureInfo.GetCultureInfo(code), tryParents: false);
        Assert.NotNull(keys);

        var missing = NeutralKeys.Except(keys).Order().ToArray();
        var unknown = keys.Except(NeutralKeys).Order().ToArray();

        Assert.True(missing.Length == 0, $"{code} is missing: {string.Join(", ", missing)}");
        Assert.True(unknown.Length == 0, $"{code} has keys English does not: {string.Join(", ", unknown)}");
    }

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void No_translation_is_left_empty(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        var blank = NeutralKeys
            .Where(key => string.IsNullOrWhiteSpace(Resources.GetString(key, culture)))
            .Order()
            .ToArray();

        Assert.True(blank.Length == 0, $"{code} is blank for: {string.Join(", ", blank)}");
    }

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void Placeholders_survive_translation(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        var wrong = new List<string>();

        foreach (var key in NeutralKeys)
        {
            var expected = Placeholders(Resources.GetString(key, CultureInfo.InvariantCulture)!);
            var actual = Placeholders(Resources.GetString(key, culture)!);
            if (!expected.SetEquals(actual))
                wrong.Add($"{key} (expected {Show(expected)}, got {Show(actual)})");
        }

        Assert.True(wrong.Count == 0, $"{code}: {string.Join("; ", wrong.Order())}");
    }

    [Theory]
    [InlineData("he")]
    [InlineData("ar")]
    public void The_right_to_left_languages_are_recognised_as_such(string code)
    {
        Localizer.Instance.SetLanguage(code);
        Assert.True(Localizer.Instance.IsRightToLeft);

        Localizer.Instance.SetLanguage("en");
        Assert.False(Localizer.Instance.IsRightToLeft);
    }

    [Fact]
    public void Language_codes_are_unique_and_named_in_their_own_language()
    {
        var codes = Localizer.Instance.Languages.Select(l => l.Code).ToArray();
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.All(Localizer.Instance.Languages, l => Assert.False(string.IsNullOrWhiteSpace(l.NativeName)));
    }

    private static HashSet<string>? KeysOf(CultureInfo culture, bool tryParents)
    {
        var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents);
        if (set is null)
            return null;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set)
            keys.Add((string)entry.Key);

        return keys;
    }

    private static HashSet<string> Placeholders(string text)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
                continue;

            var end = text.IndexOf('}', i);
            if (end > i)
                found.Add(text[i..(end + 1)]);
        }

        return found;
    }

    private static string Show(HashSet<string> placeholders)
    {
        return placeholders.Count == 0 ? "none" : string.Join(" ", placeholders.Order());
    }
}
