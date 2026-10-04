using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

internal sealed class PhraseContext(
    WordScorer scorer,
    PhraseContext.ReadInScript readInScript,
    PhraseContext.FixInOwnScript? fixInOwnScript = null,
    Script? hint = null,
    bool keysOnly = false)
{
    internal delegate ScoredWord? ReadInScript(string token, WordSource source, Script script);

    internal delegate ScoredWord? FixInOwnScript(in TokenReading token);

    private const int Quorum = 2;

    // Short words ("d", "vs") must not anchor a line.
    private const int MinAnchorLetters = 3;

    // Short chunks follow only when own-script anchors no longer hold the line.
    private const int MinPulledLettersBesideAnchors = 3;

    // Two spell-fix edits across scripts is a guess ("github" → черги); it needs a crossed neighbour.
    private const int MinEditsToNeedANeighbour = 2;

    // Frequency lead that pulls a word both languages know with no crossed neighbour: για over "gia".
    private const double MinFrequencyLeadWithoutANeighbour = 0.3;

    // An own-script typo fix must start from something that looks like the language: not σονγ → σον.
    private const double MinNaturalnessOfATypo = 0.5;

    private readonly List<TokenReading> _tokens = [];

    // Per token, read once: the typed keys never change while the line is revised.
    private readonly Dictionary<int, double?> _frequencyElsewhere = [];

    public void Add(in TokenReading token)
    {
        _tokens.Add(token);
    }

    public IReadOnlyList<TokenReading> Revise()
    {
        // Fix typos and revert islands before Direction, so neither can set it.
        List<(int Index, ScoredWord Fix)>? typos = null;
        for (var i = 0; i < _tokens.Count; i++)
            if (IsTypoLeavingItsOwnLine(i) is { } fix)
                (typos ??= []).Add((i, fix));

        if (typos is not null)
            foreach (var (i, fix) in typos)
                _tokens[i] = keysOnly
                    ? _tokens[i].Reverted()
                    : _tokens[i] with { Chosen = fix, ChosenScript = _tokens[i].Source.Script };

        List<int>? islands = null;
        for (var i = 0; i < _tokens.Count; i++)
            if (IsUncorroboratedIsland(i))
                (islands ??= []).Add(i);

        if (islands is not null)
            foreach (var i in islands)
                _tokens[i] = _tokens[i].Reverted();

        List<int>? guesses = null;
        for (var i = 0; i < _tokens.Count; i++)
            if (IsFarFetchedCrossing(i))
                (guesses ??= []).Add(i);

        if (guesses is not null)
            foreach (var i in guesses)
                _tokens[i] = _tokens[i].Reverted();

        var direction = Direction();
        if (direction is { } settled)
        {
            for (var i = 0; i < _tokens.Count; i++)
            {
                if (PullsIn(i, settled) is { } pulled)
                    _tokens[i] = _tokens[i] with { Chosen = pulled, ChosenScript = settled };
                else if (KeysAsTheyStand(_tokens[i], settled) is { } plain)
                    _tokens[i] = _tokens[i] with { Chosen = plain, ChosenScript = settled };
            }
        }

        if ((CrossedScript() ?? direction) is { } mixed)
            FollowNeighbours(mixed, direction == mixed);

        return _tokens;
    }

    // Keys-alone only; a spell-fix must not invent the line's direction.
    private static bool Crossed(in TokenReading token)
    {
        return token.IsWord && token.Chosen.Changed && token.Edits == 0 &&
               token.ChosenScript is { } into && into != token.Source.Script &&
               WordScorer.IsDictionaryHit(token.Chosen.Score);
    }

    private Script? Direction()
    {
        Script? into = null;
        var crossings = 0;

        foreach (var token in _tokens)
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

    private ScoredWord? IsTypoLeavingItsOwnLine(int index)
    {
        if (fixInOwnScript is null)
            return null;

        var token = _tokens[index];
        if (!token.IsWord || !token.Chosen.Changed || token.ChosenScript is not { } into ||
            into == token.Source.Script)
            return null;

        var anchors = 0;
        for (var i = 0; i < _tokens.Count; i++)
        {
            if (i == index)
                continue;

            var other = _tokens[i];
            if (!other.IsWord)
                continue;

            if (Crossed(other) && other.ChosenScript == into)
                return null;

            if (!other.Chosen.Changed && other.Source.Script == token.Source.Script &&
                WordScorer.IsDictionaryHit(other.Baseline) &&
                WordScanner.TrimToLetters(other.Typed, other.Source.Script).Length >= MinAnchorLetters)
                anchors++;
        }

        if (anchors < Quorum)
            return null;

        if (fixInOwnScript(token) is not { } fix)
            return null;

        // A keys-alone crossing whose own-script fix only gets there by deleting a letter is a word of the
        // other language: σονγ is "song", not σον. A same-length fix is a slip (дуеж → дуже). Restoring a
        // dropped letter lengthens the word (хеб → хлеб); that is the typo, and the other language must
        // not keep it.
        var typedLetters = WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length;
        var fixedLetters = WordScanner.TrimToLetters(fix.Text, token.Source.Script).Length;
        if (token.Edits == 0 &&
            fixedLetters < typedLetters &&
            scorer.NaturalnessOf(token.Typed, token.Source.Script, token.Source.LanguageCode) < MinNaturalnessOfATypo &&
            scorer.FrequencyOf(token.Chosen.Text, into) > scorer.FrequencyOf(fix.Text, token.Source.Script))
            return null;

        return fix;
    }

    private bool IsFarFetchedCrossing(int index)
    {
        var token = _tokens[index];
        if (!token.IsWord || !token.Chosen.Changed || token.Edits < MinEditsToNeedANeighbour ||
            token.ChosenScript is not { } into || into == token.Source.Script)
            return false;

        // A lone word keeps its guess; one beside words that stayed in their own script gives it up.
        var left = SideOf(index, -1);
        var right = SideOf(index, 1);
        return left != into && right != into && (left is not null || right is not null);
    }

    // The one script the line's keys-alone crossings went into.
    private Script? CrossedScript()
    {
        Script? into = null;
        foreach (var token in _tokens)
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
    private Script? SideOf(int index, int step)
    {
        for (var i = index + step; i >= 0 && i < _tokens.Count; i += step)
            if (SettledScriptOf(i) is { } script)
                return script;

        return null;
    }

    private Script? SettledScriptOf(int index)
    {
        var token = _tokens[index];
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
    private bool HoldsTheLine(Script script, Script against)
    {
        var mine = 0;
        var theirs = 0;
        for (var i = 0; i < _tokens.Count; i++)
        {
            var settled = SettledScriptOf(i);
            if (settled == script)
                mine++;
            else if (settled == against)
                theirs++;
        }

        return mine >= theirs;
    }

    private double? FrequencyElsewhere(int index)
    {
        if (_frequencyElsewhere.TryGetValue(index, out var known))
            return known;

        var token = _tokens[index];
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

    // A mixed line switches language mid-way, so a word both languages know goes with its neighbours:
    // "αύριο to πρωί" is Greek, "let me know πότε" keeps its English. A pulled word joins the evidence,
    // and two crossings settle the line for the short words.
    private void FollowNeighbours(Script into, bool settled)
    {
        bool moved;
        do
        {
            moved = false;
            for (var i = 0; i < _tokens.Count; i++)
            {
                if (FollowsNeighbours(i, into, settled || CrossingsInto(into) >= Quorum) is not { } pulled)
                    continue;

                _tokens[i] = _tokens[i] with { Chosen = pulled, ChosenScript = into };
                moved = true;
            }
        } while (moved);
    }

    private ScoredWord? FollowsNeighbours(int index, Script into, bool settled)
    {
        var token = _tokens[index];
        if (!token.IsWord || token.Chosen.Changed || token.IsException || token.Source.Script == into ||
            IsBlindTo(token, into))
            return null;

        if (readInScript(token.Typed, token.Source, into) is not { } pulled ||
            !WordScorer.IsDictionaryHit(pulled.Score))
            return null;

        var lead = scorer.FrequencyOf(pulled.Text, into) - token.BaselineFrequency;
        var left = SideOf(index, -1);
        var right = SideOf(index, 1);
        if (left != into && right != into)
            return lead >= MinFrequencyLeadWithoutANeighbour ? pulled : null;

        // Beside one crossing a short word is no line at all ("ghbdtn d"). At the seam it goes with the
        // language the line is written in: "στείλε μου to link" is Greek, "strength to αλήθεια" English.
        if (WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length < MinAnchorLetters &&
            (!settled || (left != right && !HoldsTheLine(into, token.Source.Script))))
            return null;

        return lead >= 0 ? pulled : null;
    }

    // Blind-pair without a source dictionary: comparison is rigged.
    private bool IsBlindTo(in TokenReading token, Script direction)
    {
        return Orthography.IsPossibleWord(token.Typed, token.Source.Script) &&
               !scorer.HasDictionaryFor(token.Source.Script) &&
               Scripts.IsBlindPair(token.Source.Script, direction);
    }

    private bool IsUncorroboratedIsland(int index)
    {
        var token = _tokens[index];
        if (!token.IsWord || !token.Chosen.Changed || token.Edits == 0)
            return false;

        if (token.ChosenScript is not { } into || into == token.Source.Script)
            return false;

        var mine = 0;
        var theirs = 0;
        var anchored = false;

        for (var i = 0; i < _tokens.Count; i++)
        {
            var other = _tokens[i];
            if (!other.IsWord)
                continue;

            if (other.Source.Script == token.Source.Script)
                mine++;
            if (other.Source.Script == into)
                theirs++;

            if (i == index)
                continue;

            if (Crossed(other) && other.ChosenScript == into)
                return false;

            if (!other.Chosen.Changed && other.Source.Script == into &&
                WordScorer.IsDictionaryHit(other.Baseline))
                anchored = true;
        }

        // "<=" so an even split reverts too; a wholly wrong-layout line has theirs == 0.
        return anchored && mine <= theirs;
    }

    private ScoredWord? KeysAsTheyStand(in TokenReading token, Script direction)
    {
        if (!token.IsWord || token.IsException || token.Edits == 0 || token.ChosenScript != direction)
            return null;

        return readInScript(token.Typed, token.Source, direction) is { Edits: 0 } plain &&
               plain.Text != token.Chosen.Text
            ? plain
            : null;
    }

    private int CrossingsInto(Script direction)
    {
        var crossings = 0;
        foreach (var token in _tokens)
            if (Crossed(token) && token.ChosenScript == direction)
                crossings++;

        return crossings;
    }

    private int Anchors(Script script)
    {
        var anchors = 0;
        foreach (var token in _tokens)
            if (token.IsWord && !token.Chosen.Changed && token.Source.Script == script &&
                WordScorer.IsDictionaryHit(token.Baseline) &&
                WordScanner.TrimToLetters(token.Typed, script).Length >= MinAnchorLetters)
                anchors++;

        return anchors;
    }

    private ScoredWord? PullsIn(int index, Script direction)
    {
        var token = _tokens[index];
        if (PullsInAnywhere(token, direction) is not { } pulled)
            return null;

        // Between two real words of its own script the line's direction is not enough: "let me know πότε".
        var own = token.Source.Script;
        return SideOf(index, -1) == own && SideOf(index, 1) == own &&
               scorer.FrequencyOf(pulled.Text, direction) - token.BaselineFrequency < MinFrequencyLeadWithoutANeighbour
            ? null
            : pulled;
    }

    private ScoredWord? PullsInAnywhere(in TokenReading token, Script direction)
    {
        // Excluded words read unchanged; without this the line would re-encode them.
        if (!token.IsWord || token.Chosen.Changed || token.Source.Script == direction || token.IsException)
            return null;

        if (WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length < MinPulledLettersBesideAnchors &&
            Anchors(token.Source.Script) is var anchors and >= Quorum && anchors > CrossingsInto(direction))
            return null;

        if (IsBlindTo(token, direction))
            return null;

        if (readInScript(token.Typed, token.Source, direction) is not { } pulled)
            return null;

        return scorer.FrequencyOf(pulled.Text, direction) >= token.BaselineFrequency ? pulled : null;
    }
}
