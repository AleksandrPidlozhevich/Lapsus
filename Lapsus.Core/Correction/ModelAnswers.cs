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
