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

    private readonly List<TokenReading> _tokens = [];

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

        if (Direction() is not { } direction)
            return _tokens;

        for (var i = 0; i < _tokens.Count; i++)
        {
            if (PullsIn(_tokens[i], direction) is { } pulled)
                _tokens[i] = _tokens[i] with { Chosen = pulled, ChosenScript = direction };
            else if (KeysAsTheyStand(_tokens[i], direction) is { } plain)
                _tokens[i] = _tokens[i] with { Chosen = plain, ChosenScript = direction };
        }

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

        return anchors >= Quorum ? fixInOwnScript(token) : null;
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

    private ScoredWord? PullsIn(in TokenReading token, Script direction)
    {
        // Excluded words read unchanged; without this the line would re-encode them.
        if (!token.IsWord || token.Chosen.Changed || token.Source.Script == direction || token.IsException)
            return null;

        if (WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length < MinPulledLettersBesideAnchors &&
            Anchors(token.Source.Script) is var anchors and >= Quorum && anchors > CrossingsInto(direction))
            return null;

        // Blind-pair without a source dictionary: comparison is rigged.
        if (Orthography.IsPossibleWord(token.Typed, token.Source.Script) &&
            !scorer.HasDictionaryFor(token.Source.Script) &&
            Scripts.IsBlindPair(token.Source.Script, direction))
            return null;

        if (readInScript(token.Typed, token.Source, direction) is not { } pulled)
            return null;

        return scorer.FrequencyOf(pulled.Text, direction) >= token.BaselineFrequency ? pulled : null;
    }
}
