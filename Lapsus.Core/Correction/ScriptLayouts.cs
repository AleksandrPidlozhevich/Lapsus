using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

internal static class ScriptLayouts
{
    private static readonly LayoutCandidate[] ToEn =
        [new(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en")];

    // List every layout of a language: same keys read differently (Apple Ukrainian, Bg BDS).
    private static readonly LayoutCandidate[] ToCyrillic =
    [
        new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk, "uk"),
        new(Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.UkApple, "uk", "uk-apple"),
        new(Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru, "ru"),
        new(Script.Cyrillic, KeyboardLayout.Be, BundledKeyboardMaps.Be, "be"),
        new(Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.Bg, "bg"),
        new(Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.BgPhonetic, "bg", "bg-phonetic"),
        new(Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.BgBds, "bg", "bg-bds"),
        new(Script.Cyrillic, KeyboardLayout.Mk, BundledKeyboardMaps.Mk, "mk")
    ];

    private static readonly LayoutCandidate[] ToGreek =
        [new(Script.Greek, KeyboardLayout.El, BundledKeyboardMaps.El, "el")];

    private static readonly LayoutCandidate[] ToHebrew =
        [new(Script.Hebrew, KeyboardLayout.He, BundledKeyboardMaps.He, "he")];

    private static readonly LayoutCandidate[] ToArabic =
        [new(Script.Arabic, KeyboardLayout.Ar, BundledKeyboardMaps.Ar, "ar")];

    private static readonly LayoutCandidate[] ToGeorgian =
        [new(Script.Georgian, KeyboardLayout.Ka, BundledKeyboardMaps.Ka, "ka")];

    private static readonly LayoutCandidate[] All =
        [.. ToEn, .. ToCyrillic, .. ToGreek, .. ToHebrew, .. ToArabic, .. ToGeorgian];

    private static readonly Dictionary<Script, LayoutCandidate[]> OtherScripts = BuildOtherScripts();

    public static IReadOnlyList<LayoutCandidate> CandidatesFor(Script script)
    {
        return OtherScripts.TryGetValue(script, out var candidates) ? candidates : All;
    }

    private static Dictionary<Script, LayoutCandidate[]> BuildOtherScripts()
    {
        var byScript = new Dictionary<Script, LayoutCandidate[]>();
        foreach (var script in Enum.GetValues<Script>())
            byScript[script] = Array.FindAll(All, c => c.ScoringScript != script);

        return byScript;
    }

    public static KeyboardMap FallbackMapFor(Script script)
    {
        return script switch
        {
            Script.Cyrillic => ToCyrillic[0].Map,
            Script.Greek => BundledKeyboardMaps.El,
            Script.Hebrew => BundledKeyboardMaps.He,
            Script.Arabic => BundledKeyboardMaps.Ar,
            Script.Georgian => BundledKeyboardMaps.Ka,
            _ => BundledKeyboardMaps.En
        };
    }

    // Exhaustive on purpose: a new layout must say what it is, not fall through.
    public static Script ScriptOf(KeyboardLayout layout)
    {
        return layout switch
        {
            KeyboardLayout.En => Script.Latin,
            KeyboardLayout.Ru or KeyboardLayout.Uk or KeyboardLayout.Be or KeyboardLayout.Bg or KeyboardLayout.Mk
                => Script.Cyrillic,
            KeyboardLayout.El => Script.Greek,
            KeyboardLayout.He => Script.Hebrew,
            KeyboardLayout.Ar => Script.Arabic,
            KeyboardLayout.Ka => Script.Georgian,
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "No script for this layout.")
        };
    }

    public static KeyboardMap MapFor(KeyboardLayout layout)
    {
        return layout switch
        {
            KeyboardLayout.En => BundledKeyboardMaps.En,
            KeyboardLayout.Ru => BundledKeyboardMaps.Ru,
            KeyboardLayout.Uk => BundledKeyboardMaps.Uk,
            KeyboardLayout.Be => BundledKeyboardMaps.Be,
            KeyboardLayout.Bg => BundledKeyboardMaps.Bg,
            KeyboardLayout.Mk => BundledKeyboardMaps.Mk,
            KeyboardLayout.El => BundledKeyboardMaps.El,
            KeyboardLayout.He => BundledKeyboardMaps.He,
            KeyboardLayout.Ar => BundledKeyboardMaps.Ar,
            KeyboardLayout.Ka => BundledKeyboardMaps.Ka,
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "No bundled map for this layout.")
        };
    }
}
