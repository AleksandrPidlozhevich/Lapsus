using Lapsus.Core.Layout;
using System.Collections.Generic;

namespace Lapsus.Settings;

public sealed record WordFormsSource(string AffixUrl, string WordsUrl, string License);

public sealed record FormListsSource(string? WiktionaryUrl, IReadOnlyList<string> UniMorphUrls, string License);

public sealed record DictionaryDescriptor(
    string Code,
    Script Script,
    string DisplayName,
    string Url,
    WordFormsSource? WordForms,
    FormListsSource? FormLists,
    string License,
    int Version)
{
    public string Title => $"{DisplayName} [{Code}]";

    public WordFormsSource? Neighbour { get; init; }
}

public static class DictionaryCatalog
{

    public const int DefaultVersion = 2;

    private const int HebrewVersion = 3;

    private const int UkrainianVersion = 3;

    private const string KaikkiUrl = "https://kaikki.org/dictionary";

    private const string UniMorphHebrewUrl =
        "https://raw.githubusercontent.com/unimorph/heb/b2bff12338caa1922e6aeea128c617730f93d334/heb";

    private const string WordFormsLicense =
        "Word forms: Wiktionary via kaikki.org, CC BY-SA 4.0 — Wiktionary contributors; " +
        "UniMorph, CC BY-SA 3.0 — Omer Goldman";

    private const string FrequencyWordsUrl =
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/525f9b560de45753a5ea01069454e72e9aa541c6/content/2018";

    private const string FrequencyWordsLicense =
        "FrequencyWords (OpenSubtitles 2018): MIT code, CC BY-SA 4.0 lists — Hermit Dave";

    private const string UnilexUrl =
        "https://raw.githubusercontent.com/lingua-libre/unilex-extended/4ac902dd82cfc1cb727fdd7205c4b4c252bdbab6/frequency-sorted-count";

    private const string UnilexLicense =
        "Unilex (sorted by lingua-libre): Unicode Data Files licence — Unicode, Inc.";

    private const string LibreOfficeUrl =
        "https://raw.githubusercontent.com/LibreOffice/dictionaries/32b006a2c22a4ac7e8ed3f03346f7b3d85a970a4";

    private const string WooormUrl =
        "https://raw.githubusercontent.com/wooorm/dictionaries/8cfea406b505e4d7df52d5a19bce525df98c54ab/dictionaries";

    private static WordFormsSource LibreOffice(string path, string license, string authors)
    {
        return new WordFormsSource($"{LibreOfficeUrl}/{path}.aff", $"{LibreOfficeUrl}/{path}.dic",
            $"Hunspell (LibreOffice): {license} — {authors}");
    }

    private static WordFormsSource Wooorm(string code, string license, string authors)
    {
        return new WordFormsSource($"{WooormUrl}/{code}/index.aff", $"{WooormUrl}/{code}/index.dic",
            $"Hunspell (wooorm/dictionaries): {license} — {authors}");
    }

    private static readonly WordFormsSource RussianHunspell = LibreOffice("ru_RU/ru_RU", "BSD", "Alexander I. Lebedev");

    private static DictionaryDescriptor FrequencyWords(
        string code, Script script, string name, WordFormsSource? wordForms = null)
    {
        return Describe(code, script, name, $"{FrequencyWordsUrl}/{code}/{code}_50k.txt", FrequencyWordsLicense,
            wordForms);
    }

    private static DictionaryDescriptor Unilex(string code, Script script, string name, WordFormsSource? wordForms = null)
    {
        return Describe(code, script, name, $"{UnilexUrl}/{code}.txt", UnilexLicense, wordForms);
    }

    private static DictionaryDescriptor Describe(
        string code, Script script, string name, string url, string listLicense, WordFormsSource? wordForms)
    {
        var license = wordForms is null ? listLicense : $"{listLicense}\n{wordForms.License}";
        return new DictionaryDescriptor(code, script, name, url, wordForms, null, license, DefaultVersion);
    }

    private static DictionaryDescriptor WithFormLists(
        string code, Script script, string name, string wiktionaryName, string uniMorphUrl, int version)
    {
        var formLists = new FormListsSource(
            $"{KaikkiUrl}/{wiktionaryName}/kaikki.org-dictionary-{wiktionaryName}.jsonl", [uniMorphUrl], WordFormsLicense);
        return new DictionaryDescriptor(code, script, name, $"{FrequencyWordsUrl}/{code}/{code}_50k.txt", null, formLists,
            $"{FrequencyWordsLicense}\n{WordFormsLicense}", version);
    }

    public static readonly IReadOnlyList<DictionaryDescriptor> Available =
    [

        FrequencyWords("uk", Script.Cyrillic, "Українська (Ukrainian)",
                LibreOffice("uk_UA/uk_UA", "MPL 1.1", "Andriy Rysin and the dict_uk project"))
            with
            {
                Neighbour = RussianHunspell,
                License = $"{FrequencyWordsLicense}; Russian words taken out with the Russian Hunspell " +
                          "(LibreOffice, BSD — Alexander I. Lebedev)\n" +
                          "Hunspell (LibreOffice): MPL 1.1 — Andriy Rysin and the dict_uk project",
                Version = UkrainianVersion
            },

        Unilex("be", Script.Cyrillic, "Беларуская (Belarusian)",
            LibreOffice("be_BY/be-official", "CC BY-SA 4.0 or LGPL 3", "Aleś Bułojčyk, Uładzimir Koščanka")),

        FrequencyWords("bg", Script.Cyrillic, "Български (Bulgarian)"),

        FrequencyWords("mk", Script.Cyrillic, "Македонски (Macedonian)"),
        FrequencyWords("ru", Script.Cyrillic, "Русский (Russian)", RussianHunspell),

        FrequencyWords("el", Script.Greek, "Ελληνικά (Greek)",
            LibreOffice("el_GR/el_GR", "MPL 1.1", "Steve Stavropoulos, Evripidis Papakostas")),

        WithFormLists("he", Script.Hebrew, "עברית (Hebrew)", "Hebrew", UniMorphHebrewUrl, HebrewVersion),

        FrequencyWords("ar", Script.Arabic, "العربية (Arabic)",
            LibreOffice("ar/ar", "MPL 1.1", "Ayaspell: Mohamed Kebdani, Taha Zerrouki")),

        Unilex("ka", Script.Georgian, "ქართული (Georgian)",
            Wooorm("ka", "MIT, built from Crúbadán (CC BY 4.0) and GeoWordsDatabase (MIT)",
                "gamag; word lists by Kevin Scannell and Dato Bumbeishvili")),

        FrequencyWords("sr", Script.Latin, "Srpski (Serbian)",
            LibreOffice("sr/sr-Latn", "LGPL 3 or MPL 2.0", "Milutin Smiljanić")),
        FrequencyWords("en", Script.Latin, "English",
            Wooorm("en", "SCOWL licence (MIT-style) and BSD (Ispell)", "Kevin Atkinson, Geoff Kuenning, Benjamin Titze")),

        FrequencyWords("de", Script.Latin, "Deutsch (German)"),
        FrequencyWords("fr", Script.Latin, "Français (French)", Wooorm("fr", "MPL 2.0", "Olivier R. (Grammalecte)")),
        FrequencyWords("es", Script.Latin, "Español (Spanish)",
            LibreOffice("es/es_ES", "MPL 1.1", "Santiago Bosio (RLA-ES)")),
        FrequencyWords("it", Script.Latin, "Italiano (Italian)"),
        FrequencyWords("pt", Script.Latin, "Português (Portuguese)",
            LibreOffice("pt_BR/pt_BR", "LGPL 3 or MPL", "Raimundo Santos Moura (VERO)")),
        FrequencyWords("nl", Script.Latin, "Nederlands (Dutch)",
            LibreOffice("nl_NL/nl_NL", "BSD or CC BY 3.0", "OpenTaal")),
        FrequencyWords("pl", Script.Latin, "Polski (Polish)",
            LibreOffice("pl_PL/pl_PL", "MPL 1.1, Apache 2.0 or CC BY 4.0", "sjp.pl, Marek Futrega")),
        FrequencyWords("cs", Script.Latin, "Čeština (Czech)"),
        FrequencyWords("sk", Script.Latin, "Slovenčina (Slovak)",
            LibreOffice("sk_SK/sk_SK", "MPL 1.1", "sk-spell, Zdenko Podobný")),
        FrequencyWords("sl", Script.Latin, "Slovenščina (Slovenian)",
            LibreOffice("sl_SI/sl_SI", "LGPL 2.1", "Amebis, Tomaž Erjavec, Aleš Košir, Primož Peterlin")),
        FrequencyWords("hr", Script.Latin, "Hrvatski (Croatian)",
            LibreOffice("hr_HR/hr_HR", "MPL 1.1", "Krunoslav Šebetić, Mirko Kos, Boris Jurić, Denis Lacković")),
        FrequencyWords("ro", Script.Latin, "Română (Romanian)", LibreOffice("ro/ro_RO", "MPL 1.1", "Rospell Team")),
        FrequencyWords("hu", Script.Latin, "Magyar (Hungarian)",
            LibreOffice("hu_HU/hu_HU", "MPL 2.0 or LGPL 3", "László Németh, Ferenc Godó")),
        FrequencyWords("tr", Script.Latin, "Türkçe (Turkish)",
            LibreOffice("tr_TR/tr_TR", "MPL 2.0", "Turkish Data Depository")),
        FrequencyWords("sv", Script.Latin, "Svenska (Swedish)", Wooorm("sv", "LGPL 3", "Göran Andersson")),
        FrequencyWords("da", Script.Latin, "Dansk (Danish)",
            LibreOffice("da_DK/da_DK", "MPL 1.1", "Foreningen for frit tilgængelige sprogværktøjer")),

        FrequencyWords("fi", Script.Latin, "Suomi (Finnish)"),
        FrequencyWords("lt", Script.Latin, "Lietuvių (Lithuanian)",
            LibreOffice("lt_LT/lt", "BSD", "Albertas Agejevas and contributors")),
        FrequencyWords("lv", Script.Latin, "Latviešu (Latvian)", LibreOffice("lv_LV/lv_LV", "LGPL 2.1", "Jānis Eisaks")),
        FrequencyWords("et", Script.Latin, "Eesti (Estonian)",
            LibreOffice("et_EE/et_EE", "LGPL 2.1 and the EKI licence",
                "Jaak Pruulmann; word list © Institute of the Estonian Language")),
        FrequencyWords("id", Script.Latin, "Bahasa Indonesia", LibreOffice("id/id_ID", "LGPL 3", "hunspell-id authors")),

        FrequencyWords("vi", Script.Latin, "Tiếng Việt (Vietnamese)")
    ];
}
