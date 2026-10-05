using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using System;
using System.Collections.Generic;

namespace Lapsus.Input;

internal static class InstalledLayoutCorrection
{
    public static PhraseCorrection Correct(
        IPhraseCorrector corrector,
        string segment,
        InstalledLayout active,
        IReadOnlyList<InstalledLayout> installed,
        KeyboardLayout? preferred,
        CorrectionHints hints = default)
    {
        if (!corrector.IsReady)
            return new PhraseCorrection(segment, segment, false, null);

        var candidates = BuildCandidates(installed);

        if (candidates.Count == 0 && !corrector.PreferAsync)
            return new PhraseCorrection(segment, segment, false, null);

        var activeSource = new LayoutSource(active.Script, active.LanguageCode, active.Map, active.LayoutId);
        var sources = new LayoutSource[installed.Count];
        for (var i = 0; i < installed.Count; i++)
            sources[i] = new LayoutSource(
                installed[i].Script, installed[i].LanguageCode, installed[i].Map, installed[i].LayoutId);

        return corrector.CorrectPhrase(segment, activeSource, sources, candidates, preferred, hints);
    }

    public static List<LayoutCandidate> BuildCandidates(IReadOnlyList<InstalledLayout> installed)
    {
        var candidates = new List<LayoutCandidate>(installed.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var layout in installed)
        {
            if (layout.Script is not { } script)
                continue;

            if (!seen.Add(layout.LayoutId))
                continue;

            candidates.Add(new LayoutCandidate(script, layout.Target, layout.Map, layout.LanguageCode,
                layout.LayoutId));
        }

        return candidates;
    }
}
