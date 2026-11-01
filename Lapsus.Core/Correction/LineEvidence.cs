using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

// What a line under revision says about itself: which way it crossed, which words anchor it, what stands
// beside a word. PhraseContext asks; only it changes the tokens.
internal sealed class LineEvidence(WordScorer scorer, PhraseContext.ReadInScript readInScript, Script? hint)
{
    public const int Quorum = 2;

    // Short words ("d", "vs") must not anchor a line.
    public const int MinAnchorLetters = 3;

    // Two spell-fix edits across scripts is a guess ("github" → черги); it needs a crossed neighbour.
    public const int MinEditsToNeedANeighbour = 2;

    // Frequency lead that pulls a word both languages know with no crossed neighbour: για over "gia".
    public const double MinFrequencyLeadWithoutANeighbour = 0.3;

    public List<TokenReading> Tokens { get; } = [];

    // Per token, read once: the typed keys never change while the line is revised.
    private readonly Dictionary<int, double?> _frequencyElsewhere = [];

    public bool IsShortAndUnmoved(int index)
    {
        var token = Tokens[index];
        return token.IsWord && !token.Chosen.Changed && !token.IsException &&
               WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length < MinAnchorLetters;
    }

    public int? WordBeside(int index, int step)
    {
        for (var i = index + step; i >= 0 && i < Tokens.Count; i += step)
            if (Tokens[i].IsWord)
                return i;

        return null;
    }

    public Script? CrossedOnTheSameKeys(int? index, in TokenReading token)
    {
        if (index is not { } i)
            return null;

        var other = Tokens[i];
        return other.Source.Script == token.Source.Script && other.Chosen.Changed &&
               other.ChosenScript is { } into && into != other.Source.Script &&
               other.Edits < MinEditsToNeedANeighbour && WordScorer.IsDictionaryHit(other.Chosen.Score)
            ? into
            : null;
    }

    // A real word of the token's own script that the other script cannot read: "oh", "fix".
    public bool IsOwnWordBeside(int? index, in TokenReading token, Script into)
    {
        if (index is not { } i)
            return false;

        var other = Tokens[i];
        if (!IsNativeWord(other, token.Source.Script))
            return false;

        return WordScanner.TrimToLetters(other.Typed, other.Source.Script).Length >= MinAnchorLetters ||
               readInScript(other.Typed, other.Source, into) is not { } reading ||
               !WordScorer.IsDictionaryHit(reading.Score);
    }

    public Script? NativeScriptOf(int? index)
    {
        return index is { } i && IsNativeWord(Tokens[i], Tokens[i].Source.Script) ? Tokens[i].Source.Script : null;
    }

    // The one script whose real words hold a line nothing crossed out of. A word of another layout that
    // slipped in spells a rarer word there ("πες μου pew όταν είσαι έτοιμος"), so nothing crossed on its
    // own; it goes with its neighbours like any word both languages know.
    public Script? HeldScript()
    {
        Script? held = null;
        var seen = new List<Script>();
        foreach (var token in Tokens)
        {
            if (!token.IsWord || seen.Contains(token.Source.Script))
                continue;

            seen.Add(token.Source.Script);
            if (Anchors(token.Source.Script) < Quorum)
                continue;

            if (held is not null)
                return null;

            held = token.Source.Script;
        }

        return seen.Count > 1 ? held : null;
    }

    // Keys-alone only; a spell-fix must not invent the line's direction.
    public static bool Crossed(in TokenReading token)
    {
        return token.IsWord && token.Chosen.Changed && token.Edits == 0 &&
               token.ChosenScript is { } into && into != token.Source.Script &&
               WordScorer.IsDictionaryHit(token.Chosen.Score);
    }

    public Script? Direction()
    {
        Script? into = null;
        var crossings = 0;

        foreach (var token in Tokens)
        {
            if (!Crossed(token))
                continue;

            var script = token.ChosenScript!.Value;
            if (into is null)
                into = script;
            else if (into != script)
                return null;

            crossings++;
        }

        if (crossings >= Quorum)
            return into;

        return hint is { } known && (into is null || into == known) ? known : null;
    }
    // The one script the line's keys-alone crossings went into.
    public Script? CrossedScript()
    {
        Script? into = null;
        foreach (var token in Tokens)
        {
            if (!Crossed(token))
                continue;

            if (into is not null && into != token.ChosenScript)
                return null;

            into = token.ChosenScript;
        }

        return into;
    }

    // Which script the nearest settled word on that side stands in: a crossing, or a real word of its own
    // script that is far commoner there than read elsewhere. Short words and near ties say nothing.
    public Script? SideOf(int index, int step)
    {
        for (var i = index + step; i >= 0 && i < Tokens.Count; i += step)
            if (SettledScriptOf(i) is { } script)
                return script;

        return null;
    }

    public Script? SettledScriptOf(int index)
    {
        var token = Tokens[index];
        if (!token.IsWord)
            return null;

        var script = token.ChosenScript ?? token.Source.Script;
        if (WordScanner.TrimToLetters(token.Chosen.Text, script).Length < MinAnchorLetters)
            return null;

        if (token.Chosen.Changed && script != token.Source.Script)
            return token.Edits < MinEditsToNeedANeighbour ? script : null;

        if (token.Chosen.Changed || !WordScorer.IsDictionaryHit(token.Baseline))
            return null;

        return FrequencyElsewhere(index) is { } elsewhere &&
               token.BaselineFrequency - elsewhere < MinFrequencyLeadWithoutANeighbour
            ? null
            : token.Source.Script;
    }

    // The language most of the line's settled words stand in.
    public bool HoldsTheLine(Script script, Script against)
    {
        var mine = 0;
        var theirs = 0;
        for (var i = 0; i < Tokens.Count; i++)
        {
            var settled = SettledScriptOf(i);
            if (settled == script)
                mine++;
            else if (settled == against)
                theirs++;
        }

        return mine >= theirs;
    }

    public double? FrequencyElsewhere(int index)
    {
        if (_frequencyElsewhere.TryGetValue(index, out var known))
            return known;

        var token = Tokens[index];
        double? best = null;
        foreach (var candidate in token.Source.Candidates)
        {
            var script = candidate.ScoringScript;
            if (script == token.Source.Script ||
                readInScript(token.Typed, token.Source, script) is not { } reading ||
                !WordScorer.IsDictionaryHit(reading.Score))
                continue;

            var frequency = scorer.FrequencyOf(reading.Text, script);
            if (best is null || frequency > best)
                best = frequency;
        }

        _frequencyElsewhere[index] = best;
        return best;
    }
    // Blind-pair without a source dictionary: comparison is rigged.
    public bool IsBlindTo(in TokenReading token, Script direction)
    {
        return Orthography.IsPossibleWord(token.Typed, token.Source.Script) &&
               !scorer.HasDictionaryFor(token.Source.Script) &&
               Scripts.IsBlindPair(token.Source.Script, direction);
    }
    public int CrossingsInto(Script direction)
    {
        var crossings = 0;
        foreach (var token in Tokens)
            if (Crossed(token) && token.ChosenScript == direction)
                crossings++;

        return crossings;
    }

    // A word on its own between spaces, sentence punctuation after it allowed: "lf," yes; the letters of
    // "m.o." or "t0" no, those are an abbreviation typed so on purpose.
    public bool StandsAlone(int index)
    {
        if (index > 0 && Tokens[index - 1] is var before && (before.IsWord || !EndsInSpace(before.Typed)))
            return false;

        if (index + 1 >= Tokens.Count)
            return true;

        var after = Tokens[index + 1];
        if (after.IsWord)
            return false;

        var rest = after.Typed.AsSpan().TrimStart(".,!?:;");
        return rest.IsEmpty ? index + 2 >= Tokens.Count : char.IsWhiteSpace(rest[0]);
    }

    private static bool EndsInSpace(string text)
    {
        return text.Length > 0 && char.IsWhiteSpace(text[^1]);
    }

    // A word typed in its own script and listed there, however short: "ну", "да" typed on the Russian layout.
    // Unlike a crossing it is no coincidence of keys, so it says which language the writer is in.
    public static bool IsNativeWord(in TokenReading token, Script script)
    {
        return token.IsWord && !token.Chosen.Changed && token.Source.Script == script &&
               WordScorer.IsDictionaryHit(token.Baseline);
    }

    // Two words in that language, crossed into it or typed in it: "ой, ye dct" is Russian (ой, все), and its
    // "ye" is ну. Crossings alone left the forgotten interjections of a mixed line behind.
    public bool IsWrittenIn(Script script)
    {
        var words = 0;
        foreach (var token in Tokens)
            if ((Crossed(token) && token.ChosenScript == script) || IsNativeWord(token, script))
                words++;

        return words >= Quorum;
    }

    public int Anchors(Script script)
    {
        var anchors = 0;
        foreach (var token in Tokens)
            if (token.IsWord && !token.Chosen.Changed && token.Source.Script == script &&
                WordScorer.IsDictionaryHit(token.Baseline) &&
                WordScanner.TrimToLetters(token.Typed, script).Length >= MinAnchorLetters)
                anchors++;

        return anchors;
    }
}
