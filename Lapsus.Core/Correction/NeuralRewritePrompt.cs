using System.Text;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public static class NeuralRewritePrompt
{
    public static string BuildSystem(KeyboardLayout? preferred = null)
    {
        var preferredHint = preferred switch
        {
            KeyboardLayout.En => " When several readings are possible, prefer English.",
            KeyboardLayout.Ru => " When several Cyrillic readings are possible, prefer Russian.",
            KeyboardLayout.Uk => " When several Cyrillic readings are possible, prefer Ukrainian.",
            KeyboardLayout.Be => " When several Cyrillic readings are possible, prefer Belarusian.",
            KeyboardLayout.Bg => " When several Cyrillic readings are possible, prefer Bulgarian.",
            KeyboardLayout.Mk => " When several Cyrillic readings are possible, prefer Macedonian.",
            KeyboardLayout.El => " When several readings are possible, prefer Greek.",
            KeyboardLayout.He => " When several readings are possible, prefer Hebrew.",
            KeyboardLayout.Ar => " When several readings are possible, prefer Arabic.",
            KeyboardLayout.Ka => " When several readings are possible, prefer Georgian.",
            _ => string.Empty
        };

        // Small instruct models drift into translating or chatting.
        return
            "You repair text typed with the wrong keyboard layout — the user hit the right physical " +
            "keys but in the wrong language (e.g. \"ntrcn\" was meant to be \"текст\") — and fix simple typos. " +
            "Rules: reply with ONLY the corrected text and nothing else. Never translate, transliterate, " +
            "explain, or add quotes. Keep the original spacing, punctuation, capitalization and word order. " +
            "If the text is already a real word in its language, repeat it unchanged." + preferredHint;
    }

    public static string BuildUser(
        string text,
        KeyboardMap sourceMap,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred = null,
        IReadOnlyList<string>? remaps = null)
    {
        remaps ??= CollectRemaps(text, sourceMap, candidates, preferred);
        var examples = BuildExamples(preferred, candidates);

        var sb = new StringBuilder();
        sb.AppendLine("Fix the wrong-layout text and any typo. Reply with ONLY the corrected text.");
        sb.AppendLine("Examples (input => corrected):");
        sb.Append(examples);
        if (remaps.Count > 0)
        {
            // Include "leave it alone"; otherwise a correct word is an instruction to rewrite it.
            sb.Append("The intended text is one of: ");
            sb.AppendJoin(", ", remaps);
            sb.AppendLine(
                " — or the text below exactly as it stands, if that is already a real word. " +
                "Pick the real word(s); fix a small typo only if needed.");
        }

        sb.Append("Text: ").Append(text).AppendLine();
        sb.Append("Corrected:");
        return sb.ToString();
    }

    public static IReadOnlyList<string> CollectRemaps(
        string text,
        KeyboardMap sourceMap,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred = null)
    {
        var core = text.Trim();
        var remaps = new List<string>();

        void Add(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                string.Equals(value, core, StringComparison.Ordinal) ||
                remaps.Contains(value, StringComparer.Ordinal))
                return;
            remaps.Add(value);
        }

        if (preferred is not null)
            foreach (var c in candidates)
            {
                if (c.Target != preferred)
                    continue;
                Add(LayoutTranscoder.Transcode(core, sourceMap, c.Map));
            }

        foreach (var c in candidates)
            Add(LayoutTranscoder.Transcode(core, sourceMap, c.Map));

        return remaps;
    }

    private const int MaxExampleScripts = 3;

    private const int PairsPerExtraScript = 2;

    private const string LatinNoOp = "hello => hello\n";

    private static string BuildExamples(
        KeyboardLayout? preferred, IReadOnlyList<LayoutCandidate> candidates)
    {
        var scripts = TargetScripts(preferred, candidates);
        var sb = new StringBuilder();

        var perScript = scripts.Count == 1 ? int.MaxValue : PairsPerExtraScript;
        foreach (var script in scripts)
            AppendPairs(sb, IntoScript(script, preferred), perScript);

        sb.Append(LatinNoOp);

        sb.Append(OutOfScript(scripts[0]));
        return sb.ToString();
    }

    private static void AppendPairs(StringBuilder sb, string block, int limit)
    {
        var taken = 0;
        foreach (var pair in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (taken == limit)
                return;

            sb.Append(pair).Append('\n');
            taken++;
        }
    }

    private static IReadOnlyList<Script> TargetScripts(
        KeyboardLayout? preferred, IReadOnlyList<LayoutCandidate> candidates)
    {
        var scripts = new List<Script>();

        void Add(Script script)
        {
            if (script != Script.Latin && !scripts.Contains(script) && scripts.Count < MaxExampleScripts)
                scripts.Add(script);
        }

        if (preferred is { } chosen)
            Add(ScriptLayouts.ScriptOf(chosen));

        foreach (var candidate in candidates)
            if (candidate.ScoringScript == Script.Cyrillic)
                Add(Script.Cyrillic);

        foreach (var candidate in candidates)
            Add(candidate.ScoringScript);

        if (scripts.Count == 0)
            scripts.Add(Script.Cyrillic);

        return scripts;
    }

    private static string IntoScript(Script target, KeyboardLayout? preferred)
    {
        switch (target)
        {
            case Script.Georgian:
                return
                    "gamarjoba => გამარჯობა\n" +
                    "qarTuli => ქართული\n" +
                    "wigni => წიგნი\n";

            case Script.Greek:
                return
                    "geia => γεια\n" +
                    "kal;a => καλά\n" +
                    "kai => και\n";

            case Script.Hebrew:
                return
                    "akuo => שלום\n" +
                    "yexy => טקסט\n" +
                    ",usv => תודה\n";

            case Script.Arabic:
                return
                    "lvpfh => مرحبا\n" +
                    "sghl => سلام\n" +
                    "a;vh => شكرا\n";

            default:
                // Russian only when that layout is the one the user asked to prefer.
                // Otherwise stay on letters shared by the ЙЦУКЕН family, so the model
                // is not taught that Cyrillic means Russian.
                if (preferred is KeyboardLayout.Ru)
                    return
                        "ntrcn => текст\n" +
                        "ghjcnj => просто\n" +
                        "ghbdtn => привет\n" +
                        "cfqn => сайт\n";

                return
                    "ntrcn => текст\n" +
                    "jl => од\n" +
                    "vjkjrj => молоко\n" +
                    "cfqn => сайт\n";
        }
    }

    private static string OutOfScript(Script source)
    {
        return source switch
        {
            Script.Greek =>
                "σηιπ => ship\n" +
                "τεχτ => text\n" +
                "καλημέρα => καλημέρα\n",

            Script.Hebrew =>
                "דיןפ => ship\n" +
                "אקסא => text\n" +
                "שלום => שלום\n",

            Script.Arabic =>
                "ساهح => ship\n" +
                "فثءف => text\n" +
                "مرحبا => مرحبا\n",

            // Phonetic layout looks like transliteration; the model must re-read the keys.
            Script.Georgian =>
                "სჰიპ => ship\n" +
                "ტეხტ => text\n" +
                "გამარჯობა => გამარჯობა\n",

            _ =>
                "сфк => car\n" +
                "сфе => cat\n" +
                "руддщ => hello\n" +
                "текст => текст\n"
        };
    }
}
