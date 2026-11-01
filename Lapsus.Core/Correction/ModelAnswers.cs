using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

internal static class ModelAnswers
{
    // A letter in a script the user neither typed nor has a layout for is a translation, not a repair.
    public static bool WritesOnlyTypableScripts(
        string text, string rewritten, LayoutSource active, IReadOnlyList<LayoutCandidate> candidates)
    {
        foreach (var ch in rewritten)
        {
            if (Scripts.Of(ch) is not { } script || script == active.Script)
                continue;

            if (!candidates.Any(c => c.ScoringScript == script) && !text.Any(c => Scripts.Of(c) == script))
                return false;
        }

        return true;
    }
    public static bool IsEcho(string rewritten, string text)
    {
        return string.Equals(rewritten, text, StringComparison.Ordinal) ||
               string.Equals(rewritten, text.Trim(), StringComparison.Ordinal);
    }

    public static string RestoreOuterWhitespace(string original, string rewritten)
    {
        var trimmed = original.AsSpan().Trim();
        if (trimmed.Length == original.Length || trimmed.Length == 0)
            return rewritten;

        var leading = original.Length - original.AsSpan().TrimStart().Length;
        var trailing = original.Length - original.AsSpan().TrimEnd().Length;
        return string.Concat(original.AsSpan(0, leading), rewritten, original.AsSpan(original.Length - trailing));
    }

    public static bool LooksLikeWrongLayoutRun(string text)
    {
        if (Scripts.Dominant(text) is not { } script)
            return false;

        if (!Orthography.IsPossibleWord(text, script))
            return true;

        if (Scripts.IsScoringBlind(script))
            return false;

        var letters = 0;
        var vowels = 0;
        foreach (var ch in text)
        {
            if (!Alphabets.IsLetterOf(script, ch))
                continue;

            letters++;
            if (Alphabets.IsVowelOf(script, ch))
                vowels++;
        }

        return letters >= 3 && vowels == 0;
    }

    public static string SanitizeModelOutput(string raw, string original)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var s = raw.Trim();

        // Refuse multi-paragraph chatter.
        var nl = s.IndexOfAny(['\r', '\n']);
        if (nl >= 0)
            s = s[..nl].Trim();

        if (s.Length >= 2 &&
            ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();

        if (s.StartsWith("=>", StringComparison.Ordinal))
            s = s[2..].Trim();

        foreach (var prefix in new[]
                 {
                     "Corrected:", "Correction:", "Output:", "Answer:", "Result:",
                     "Исправленный текст:", "Исправлено:", "Ответ:"
                 })
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[prefix.Length..].Trim();
                break;
            }

        var arrowIdx = s.LastIndexOf("=>", StringComparison.Ordinal);
        var arrowLen = 2;
        if (arrowIdx < 0)
        {
            arrowIdx = s.LastIndexOf("→", StringComparison.Ordinal);
            arrowLen = 1;
        }

        if (arrowIdx >= 0 && arrowIdx + arrowLen < s.Length)
        {
            var right = s[(arrowIdx + arrowLen)..].Trim();
            if (right.Length > 0)
                s = right;
        }

        if (s.Length > Math.Max(original.Length * 3, original.Length + 40))
            return string.Empty;

        return s;
    }

    public static (KeyboardLayout? Target, string? LayoutId) ResolveTarget(
        string original,
        string corrected,
        LayoutSource active,
        IReadOnlyList<LayoutCandidate> candidates,
        KeyboardLayout? preferred)
    {
        var originalScript = Scripts.Dominant(original) ?? active.Script;
        if (MovedInto(original, corrected, originalScript) is not { } correctedScript)
            return (null, null);

        LayoutCandidate? match = null;
        foreach (var c in candidates)
        {
            if (c.ScoringScript != correctedScript)
                continue;

            if (preferred is not null && c.Target == preferred)
            {
                match = c;
                break;
            }

            match ??= c;
        }

        return match is null ? (null, null) : (match.Value.Target, match.Value.LayoutId);
    }

    private static Script? MovedInto(string original, string corrected, Script? originalScript)
    {
        var before = original.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var after = corrected.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (before.Length == 0 || before.Length != after.Length)
        {
            var whole = Scripts.Dominant(corrected);
            return whole is null || whole == originalScript ? null : whole;
        }

        Script? into = null;
        for (var i = 0; i < before.Length; i++)
        {
            var to = Scripts.Dominant(after[i]);
            if (to is null || to == Scripts.Dominant(before[i]))
                continue;

            if (into is not null && into != to)
                return null;

            into = to;
        }

        return into;
    }
}
