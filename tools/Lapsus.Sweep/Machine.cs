using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Sweep;

internal sealed record TargetLanguage(
    string Code, string Name, Script Script, KeyboardLayout Layout, KeyboardMap Map, string? Variant = null)
{
    public static readonly TargetLanguage[] All =
    [
        new("uk", "Ukrainian", Script.Cyrillic, KeyboardLayout.Uk, BundledKeyboardMaps.Uk),
        new("he", "Hebrew", Script.Hebrew, KeyboardLayout.He, BundledKeyboardMaps.He),
        new("ar", "Arabic", Script.Arabic, KeyboardLayout.Ar, BundledKeyboardMaps.Ar),
        new("el", "Greek", Script.Greek, KeyboardLayout.El, BundledKeyboardMaps.El),
        new("bg", "Bulgarian", Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.Bg, "phonetic"),
        new("bg", "Bulgarian", Script.Cyrillic, KeyboardLayout.Bg, BundledKeyboardMaps.BgBds, "bds"),
        new("ru", "Russian", Script.Cyrillic, KeyboardLayout.Ru, BundledKeyboardMaps.Ru),
        new("be", "Belarusian", Script.Cyrillic, KeyboardLayout.Be, BundledKeyboardMaps.Be),
        new("mk", "Macedonian", Script.Cyrillic, KeyboardLayout.Mk, BundledKeyboardMaps.Mk),
        new("ka", "Georgian", Script.Georgian, KeyboardLayout.Ka, BundledKeyboardMaps.Ka)
    ];

    public static readonly TargetLanguage[] Priority =
        [.. All.Where(l => l.Code is "uk" or "he" or "ar" or "el" or "bg" or "be" or "ru")];

    public string Key => Variant is null ? Code : $"{Code}-{Variant}";

    public string Label => Variant is null ? $"{Name} ({Code})" : $"{Name} ({Code}, {Variant} keyboard)";

    public string ScriptName => Script.ToString();

    public bool CanType(string word)
    {
        foreach (var ch in word)
            if (!Map.TryGetKey(ch, out _, out _) && !Map.TryDecompose(ch, out _, out _, out _))
                return false;

        return true;
    }

    public static TargetLanguage[] ByCode(string code)
    {
        return Array.FindAll(All, l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(l.Key, code, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class Machine
{
    public static readonly LayoutSource En =
        new(Script.Latin, "en", BundledKeyboardMaps.En, "en-US");

    private readonly LayoutCorrector _with;
    private readonly LayoutCorrector _without;
    private readonly SpellChecker _spell;

    public Machine(SpellChecker spell, TargetLanguage target, double punctuationHeadStart = LayoutCorrector.DefaultPunctuationHeadStart)
    {
        _spell = spell;
        Target = target;
        TargetSource = new LayoutSource(target.Script, target.Code, target.Map, $"{target.Code}-id");
        Installed = [En, TargetSource];
        Candidates =
        [
            new LayoutCandidate(Script.Latin, KeyboardLayout.En, BundledKeyboardMaps.En, "en", "en-US"),
            new LayoutCandidate(target.Script, target.Layout, target.Map, target.Code, $"{target.Code}-id")
        ];
        _with = new LayoutCorrector(spell, punctuationHeadStart: punctuationHeadStart);
        _without = new LayoutCorrector(spell, phraseContext: false, punctuationHeadStart: punctuationHeadStart);
    }

    public TargetLanguage Target { get; }

    // The dictionary brain with phrase context, as the app builds it and hands to the neural one as adviser.
    public LayoutCorrector Brain => _with;

    public LayoutSource TargetSource { get; }

    public LayoutSource[] Installed { get; }

    public LayoutCandidate[] Candidates { get; }

    public PhraseCorrection Correct(
        string text, bool phraseContext, LayoutSource? active = null, CorrectionHints hints = default)
    {
        var brain = phraseContext ? _with : _without;
        return brain.CorrectPhrase(text, active ?? En, Installed, Candidates, null, hints);
    }

    public bool Knows(string word)
    {
        return _spell.IsKnownWord(word, Target.Script, Target.Code);
    }

    public bool KnowsStem(string word)
    {
        return _spell.IsKnownStem(word, Target.Script, Target.Code, out _);
    }

    // A slip that spells a real word of the language (list or Hunspell) is no typo a spell-fix can see.
    public bool IsAnyWord(string word)
    {
        return _spell.IsKnownWord(word, Target.Script) || _spell.IsLexiconWord(word, Target.Script);
    }

    public bool KnowsEnglish(string word)
    {
        return _spell.IsKnownWord(word, Script.Latin, "en");
    }

    public string AsEnglishKeystrokes(string text)
    {
        return LayoutTranscoder.Transcode(text, Target.Map, BundledKeyboardMaps.En);
    }

    public string AsTargetKeystrokes(string text)
    {
        return LayoutTranscoder.Transcode(text, BundledKeyboardMaps.En, Target.Map);
    }

    public static string[] Words(string line)
    {
        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}

internal static class Report
{
    private static readonly Dictionary<string, Metric> Recorded = new();
    private static string _heading = string.Empty;

    public static IReadOnlyDictionary<string, Metric> Metrics => Recorded;

    public static void Reset()
    {
        Recorded.Clear();
        _heading = string.Empty;
    }

    public static void Heading(string title)
    {
        _heading = title;
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine(new string('─', title.Length));
    }

    public static void Row(string label, double without, double with, Better better)
    {
        var arrow = Math.Abs(with - without) < 0.0005 ? "  " : with > without ? "↑ " : "↓ ";
        Console.WriteLine($"  {label,-46} {without,7:P1} → {arrow}{with,7:P1}");
        Record($"{label} · without", without, better);
        Record($"{label} · with", with, better);
    }

    public static void Value(string label, double value, Better better)
    {
        Console.WriteLine($"  {label,-46} {value,7:P1}");
        Record(label, value, better);
    }

    public static void Cells(string label, IReadOnlyList<string> names, IReadOnlyList<double> values, Better better)
    {
        var cells = new string[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            cells[i] = $"{names[i]} {values[i],6:P1}";
            Record($"{label} · {names[i]}", values[i], better);
        }

        Console.WriteLine($"  {label,-46} {string.Join("   ", cells)}");
    }

    public static void Line(string label, string value)
    {
        Console.WriteLine($"  {label,-46} {value}");
    }

    private static void Record(string label, double value, Better better)
    {
        Recorded[$"{_heading} · {label}"] = new Metric(Math.Round(value, 4), better);
    }
}
