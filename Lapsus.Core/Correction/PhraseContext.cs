using Lapsus.Core.Layout;
using static Lapsus.Core.Correction.LineEvidence;

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

    // Short chunks follow only when own-script anchors no longer hold the line.
    private const int MinPulledLettersBesideAnchors = 3;

    // An own-script typo fix must start from something that looks like the language: not σονγ → σον.
    private const double MinNaturalnessOfATypo = 0.5;

    private readonly LineEvidence _line = new(scorer, readInScript, hint);

    private List<TokenReading> Tokens => _line.Tokens;

    public void Add(in TokenReading token)
    {
        Tokens.Add(token);
    }

    public IReadOnlyList<TokenReading> Revise()
    {
        // Fix typos and revert islands before Direction, so neither can set it.
        List<(int Index, ScoredWord Fix)>? typos = null;
        for (var i = 0; i < Tokens.Count; i++)
            if (IsTypoLeavingItsOwnLine(i) is { } fix)
                (typos ??= []).Add((i, fix));

        if (typos is not null)
            foreach (var (i, fix) in typos)
                Tokens[i] = keysOnly
                    ? Tokens[i].Reverted()
                    : Tokens[i] with { Chosen = fix, ChosenScript = Tokens[i].Source.Script };

        for (var i = 0; i < Tokens.Count; i++)
        {
            if (IsTermAmongAnotherLanguage(i))
                Tokens[i] = Tokens[i].Reverted();
            else if (SameScriptLayoutInstead(i) is { } sibling)
                Tokens[i] = Tokens[i] with { Chosen = sibling, ChosenScript = Tokens[i].Source.Script };
        }

        List<int>? islands = null;
        for (var i = 0; i < Tokens.Count; i++)
            if (IsUncorroboratedIsland(i))
                (islands ??= []).Add(i);

        if (islands is not null)
            foreach (var i in islands)
                Tokens[i] = Tokens[i].Reverted();

        List<int>? guesses = null;
        for (var i = 0; i < Tokens.Count; i++)
            if (IsFarFetchedCrossing(i))
                (guesses ??= []).Add(i);

        if (guesses is not null)
            foreach (var i in guesses)
                Tokens[i] = Tokens[i].Reverted();

        var direction = _line.Direction();
        if (direction is { } settled)
        {
            for (var i = 0; i < Tokens.Count; i++)
            {
                if (PullsIn(i, settled) is { } pulled)
                    Tokens[i] = Tokens[i] with { Chosen = pulled, ChosenScript = settled };
                else if (KeysAsTheyStand(Tokens[i], settled) is { } plain)
                    Tokens[i] = Tokens[i] with { Chosen = plain, ChosenScript = settled };
            }
        }

        if ((_line.CrossedScript() ?? direction) is { } mixed)
            FollowNeighbours(mixed, direction == mixed);
        else if (_line.HeldScript() is { } held)
            FollowNeighbours(held, false, listedOnly: true);

        FollowRuns();

        return Tokens;
    }

    // A short word both languages know goes the way of the words typed next to it on the same keys: in
    // "ye b ult pull request" ult crossed to где, so b is и and ye is ну. One crossing settles no line, but it
    // does settle the word typed right beside it. A word typed right in the other script ("ну vs code") marks
    // a switch the writer made, not one forgotten, so it pulls nothing.
    private void FollowRuns()
    {
        bool moved;
        do
        {
            moved = false;
            for (var i = 0; i < Tokens.Count; i++)
            {
                if (FollowsItsRun(i) is not { } follow)
                    continue;

                Tokens[i] = Tokens[i] with { Chosen = follow.Pulled, ChosenScript = follow.Into };
                moved = true;
            }
        } while (moved);
    }

    private (ScoredWord Pulled, Script Into)? FollowsItsRun(int index)
    {
        if (!_line.IsShortAndUnmoved(index))
            return null;

        // Keys with no letter of their own ("'[", "'") are no word where typed, so nothing holds them there.
        var token = Tokens[index];
        var letterless = WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length == 0;
        if (!letterless && (token.BaselineFrequency <= 0 || !_line.StandsAlone(index)))
            return null;

        var left = _line.WordBeside(index, -1);
        var right = _line.WordBeside(index, 1);
        var fromLeft = _line.CrossedOnTheSameKeys(left, token);
        var fromRight = _line.CrossedOnTheSameKeys(right, token);
        if (fromLeft is not null && fromRight is not null && fromLeft != fromRight)
            return null;

        if ((fromLeft ?? fromRight) is not { } into)
            return letterless ? LetterlessBesideItsLanguage(index, left, right) : OnlyShortWordsBeside(index);

        var reading = letterless ? ReadLetterless(token, into) : readInScript(token.Typed, token.Source, into);
        if (_line.IsBlindTo(token, into) || reading is not { } pulled || !WordScorer.IsDictionaryHit(pulled.Score))
            return null;

        // The commoner word must not give way: "in" next to a crossing is still "in", never шт.
        var lead = scorer.FrequencyOf(pulled.Text, into) - token.BaselineFrequency;
        if (lead < 0)
            return null;

        // At a seam between a real word of its own and the crossing, only a far commoner reading moves:
        // "oh, ye 'nj fix" is "oh, ну это fix", ye being a rare word and ну a common one.
        var seam = _line.IsOwnWordBeside(left, token, into) || _line.IsOwnWordBeside(right, token, into);
        return seam && lead < MinFrequencyLeadWithoutANeighbour ? null : (pulled, into);
    }

    // "'[, ладно": letterless keys beside a word typed right in another script are a word of that script when
    // they spell one there (эх).
    private (ScoredWord Pulled, Script Into)? LetterlessBesideItsLanguage(int index, int? left, int? right)
    {
        var token = Tokens[index];
        if ((_line.NativeScriptOf(left) ?? _line.NativeScriptOf(right)) is not { } into || into == token.Source.Script ||
            ReadLetterless(token, into) is not { } pulled)
            return null;

        return (pulled, into);
    }

    // "'[," is эх and a comma: the chunk runs to the space, so a sentence mark typed after it is read off first
    // and kept as typed when the keys alone spell no word.
    private ScoredWord? ReadLetterless(in TokenReading token, Script into)
    {
        if (readInScript(token.Typed, token.Source, into) is { } whole && WordScorer.IsDictionaryHit(whole.Score))
            return whole;

        var core = token.Typed.TrimEnd('.', ',', '!', '?');
        if (core.Length == 0 || core.Length == token.Typed.Length ||
            readInScript(core, token.Source, into) is not { } pulled || !WordScorer.IsDictionaryHit(pulled.Score))
            return null;

        return pulled with { Text = pulled.Text + token.Typed[core.Length..] };
    }

    // "ye jr, давай": a run of short words, every one of them both languages' and next to a word typed right in
    // the other script, in a line with no real word of their own. They go to that script when far commoner
    // there. A single short word stays as typed: "Спартак vs ЦСКА".
    private (ScoredWord Pulled, Script Into)? OnlyShortWordsBeside(int index)
    {
        var token = Tokens[index];
        var first = index;
        while (_line.WordBeside(first, -1) is { } before && Tokens[before].Source.Script == token.Source.Script)
            first = before;

        var last = index;
        while (_line.WordBeside(last, 1) is { } after && Tokens[after].Source.Script == token.Source.Script)
            last = after;

        var run = 0;
        for (var i = first; i <= last; i++)
        {
            if (!Tokens[i].IsWord)
                continue;

            if (!_line.IsShortAndUnmoved(i))
                return null;
            run++;
        }

        if (run < Quorum || _line.Anchors(token.Source.Script) > 0)
            return null;

        if ((_line.NativeScriptOf(_line.WordBeside(first, -1)) ?? _line.NativeScriptOf(_line.WordBeside(last, 1))) is not { } into ||
            into == token.Source.Script || _line.IsBlindTo(token, into))
            return null;

        if (readInScript(token.Typed, token.Source, into) is not { } pulled || !WordScorer.IsDictionaryHit(pulled.Score))
            return null;

        return scorer.FrequencyOf(pulled.Text, into) - token.BaselineFrequency >= MinFrequencyLeadWithoutANeighbour
            ? (pulled, into)
            : null;
    }

    private ScoredWord? IsTypoLeavingItsOwnLine(int index)
    {
        if (fixInOwnScript is null)
            return null;

        var token = Tokens[index];
        if (!token.IsWord || !token.Chosen.Changed || token.ChosenScript is not { } into ||
            into == token.Source.Script)
            return null;

        var anchors = 0;
        for (var i = 0; i < Tokens.Count; i++)
        {
            if (i == index)
                continue;

            var other = Tokens[i];
            if (!other.IsWord)
                continue;

            if (LineEvidence.Crossed(other) && other.ChosenScript == into)
                return null;

            // A word typed right in that language says the writer is writing it: "ну да, это был deploy yf
            // prod" is на, not "of".
            if (LineEvidence.IsNativeWord(other, into))
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

    // A lone word of its script, respelled as a typo, among words typed right in another: a term the writer
    // switched layouts for ("через api", "просто hotfix"), not a slip. Respelling it (ali, hottie) breaks it.
    private bool IsTermAmongAnotherLanguage(int index)
    {
        var token = Tokens[index];
        if (!token.IsWord || !token.Chosen.Changed || token.Edits == 0 || token.ChosenScript != token.Source.Script)
            return false;

        var others = 0;
        for (var i = 0; i < Tokens.Count; i++)
        {
            var other = Tokens[i];
            if (i == index || !other.IsWord)
                continue;

            // Anything else left in this script makes it a line of this language, where a typo is a typo.
            if (other.Source.Script == token.Source.Script && other.ChosenScript == token.Source.Script)
                return false;

            if (other.Source.Script != token.Source.Script && LineEvidence.IsNativeWord(other, other.Source.Script))
                others++;
        }

        return others >= Quorum;
    }

    // "ой, це ынше": a Ukrainian word typed on the Russian layout reads as English "shit" too, and the word
    // alone takes the commoner. Nothing in the line backs English while its own words are Cyrillic, so the
    // other Cyrillic layout's reading, інше, is the one meant.
    private ScoredWord? SameScriptLayoutInstead(int index)
    {
        // Keys alone or with a spell-fix on top ("ынше" is "syit", one slip from shit): either way a crossing.
        var token = Tokens[index];
        if (!token.IsWord || !token.Chosen.Changed || token.ChosenScript is not { } into || into == token.Source.Script ||
            !WordScorer.IsDictionaryHit(scorer.Score(token.Chosen.Text, into)))
            return null;

        var own = 0;
        for (var i = 0; i < Tokens.Count; i++)
        {
            if (i == index || !Tokens[i].IsWord)
                continue;

            if ((LineEvidence.Crossed(Tokens[i]) && Tokens[i].ChosenScript == into) || LineEvidence.IsNativeWord(Tokens[i], into))
                return null;

            if (LineEvidence.IsNativeWord(Tokens[i], token.Source.Script))
                own++;
        }

        if (own < Quorum)
            return null;

        ScoredWord? best = null;
        foreach (var candidate in token.Source.Candidates)
        {
            if (candidate.ScoringScript != token.Source.Script || ReferenceEquals(candidate.Map, token.Source.Map))
                continue;

            var text = LayoutTranscoder.Transcode(token.Typed, token.Source.Map, candidate.Map);
            if (text == token.Typed)
                continue;

            var score = scorer.Score(text, candidate.ScoringScript, candidate.LanguageCode);
            if (WordScorer.IsDictionaryHit(score) && (best is null || score > best.Value.Score))
                best = new ScoredWord(text, candidate.Target, candidate.LayoutId, score, true);
        }

        return best;
    }

    private bool IsFarFetchedCrossing(int index)
    {
        var token = Tokens[index];
        if (!token.IsWord || !token.Chosen.Changed || token.Edits < MinEditsToNeedANeighbour ||
            token.ChosenScript is not { } into || into == token.Source.Script)
            return false;

        // A lone word keeps its guess; one beside words that stayed in their own script gives it up.
        var left = _line.SideOf(index, -1);
        var right = _line.SideOf(index, 1);
        return left != into && right != into && (left is not null || right is not null);
    }

    // A mixed line switches language mid-way, so a word both languages know goes with its neighbours:
    // "αύριο to πρωί" is Greek, "let me know πότε" keeps its English. A pulled word joins the evidence,
    // and two words in that language settle the line for the short words.
    private void FollowNeighbours(Script into, bool settled, bool listedOnly = false)
    {
        bool moved;
        do
        {
            moved = false;
            for (var i = 0; i < Tokens.Count; i++)
            {
                if (listedOnly && IsKnownWithoutACount(Tokens[i]))
                    continue;

                if (FollowsNeighbours(i, into, settled || _line.CrossingsInto(into) >= Quorum) is not { } pulled)
                    continue;

                Tokens[i] = Tokens[i] with { Chosen = pulled, ChosenScript = into };
                moved = true;
            }
        } while (moved);
    }

    // A word only Hunspell knows has no frequency to weigh against the other reading's, so any listed
    // reading would look commoner. Where nothing in the line crossed, that is not enough to move it.
    private static bool IsKnownWithoutACount(in TokenReading token)
    {
        return WordScorer.IsDictionaryHit(token.Baseline) && token.BaselineFrequency <= 0;
    }

    private ScoredWord? FollowsNeighbours(int index, Script into, bool settled)
    {
        var token = Tokens[index];
        if (!token.IsWord || token.Chosen.Changed || token.IsException || token.Source.Script == into ||
            _line.IsBlindTo(token, into))
            return null;

        if (readInScript(token.Typed, token.Source, into) is not { } pulled ||
            !WordScorer.IsDictionaryHit(pulled.Score))
            return null;

        var lead = scorer.FrequencyOf(pulled.Text, into) - token.BaselineFrequency;
        var left = _line.SideOf(index, -1);
        var right = _line.SideOf(index, 1);
        var isShort = WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length < MinAnchorLetters;

        // Words typed right in that language settle the line too, but only for a plain word both lists know
        // ("lf" is да). One its own list lacks, or an abbreviation ("m.o.", "t0"), was typed so on purpose.
        settled = settled || (token.BaselineFrequency > 0 && _line.StandsAlone(index) && _line.IsWrittenIn(into));

        // Among short words only ("ой, nf ye"), a line already written in that language takes them along.
        if (isShort && settled && left is null && right is null)
            return lead >= 0 ? pulled : null;

        // With no neighbour in that language only a long word moves, on frequency alone: για over "gia". A short
        // one typed right stays: "ну vs code" keeps its "vs" though мы is far commoner.
        if (left != into && right != into)
            return !isShort && lead >= MinFrequencyLeadWithoutANeighbour ? pulled : null;

        // Beside one crossing a short word is no line at all ("ghbdtn d"). At the seam it goes with the
        // language the line is written in: "στείλε μου to link" is Greek, "strength to αλήθεια" English.
        if (isShort && (!settled || (left != right && !_line.HoldsTheLine(into, token.Source.Script))))
            return null;

        return lead >= 0 ? pulled : null;
    }

    private bool IsUncorroboratedIsland(int index)
    {
        var token = Tokens[index];
        if (!token.IsWord || !token.Chosen.Changed || token.Edits == 0)
            return false;

        if (token.ChosenScript is not { } into || into == token.Source.Script)
            return false;

        var mine = 0;
        var theirs = 0;
        var anchored = false;

        for (var i = 0; i < Tokens.Count; i++)
        {
            var other = Tokens[i];
            if (!other.IsWord)
                continue;

            if (other.Source.Script == token.Source.Script)
                mine++;
            if (other.Source.Script == into)
                theirs++;

            if (i == index)
                continue;

            if (LineEvidence.Crossed(other) && other.ChosenScript == into)
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

    private ScoredWord? PullsIn(int index, Script direction)
    {
        var token = Tokens[index];
        var sandwiched = _line.SideOf(index, -1) == direction && _line.SideOf(index, 1) == direction;
        if (PullsInAnywhere(token, direction, sandwiched) is not { } pulled)
            return null;

        // Between two real words of its own script the line's direction is not enough: "let me know πότε".
        var own = token.Source.Script;
        return _line.SideOf(index, -1) == own && _line.SideOf(index, 1) == own &&
               scorer.FrequencyOf(pulled.Text, direction) - token.BaselineFrequency < MinFrequencyLeadWithoutANeighbour
            ? null
            : pulled;
    }

    private ScoredWord? PullsInAnywhere(in TokenReading token, Script direction, bool sandwiched = false)
    {
        // Excluded words read unchanged; without this the line would re-encode them.
        if (!token.IsWord || token.Chosen.Changed || token.Source.Script == direction || token.IsException)
            return null;

        if (WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length < MinPulledLettersBesideAnchors &&
            _line.Anchors(token.Source.Script) is var anchors and >= Quorum && anchors > _line.CrossingsInto(direction))
            return null;

        if (_line.IsBlindTo(token, direction))
            return null;

        if (readInScript(token.Typed, token.Source, direction) is not { } pulled)
            return null;

        if (scorer.FrequencyOf(pulled.Text, direction) >= token.BaselineFrequency)
            return pulled;

        // Between two crossings a word about as common there goes with them whatever stands elsewhere in the
        // line: "καλά, λετ με ψηεψκ" is "let me check" though καλά keeps its Greek. A scrap does not: "ρεωιες
        // στο ψοδε" keeps στο, which reads as the rare "sto".
        if (sandwiched && WordScorer.IsDictionaryHit(pulled.Score) &&
            scorer.FrequencyOf(pulled.Text, direction) - token.BaselineFrequency > -MinFrequencyLeadWithoutANeighbour)
            return pulled;

        // The commoner reading keeps a word only while its script still holds part of the line: with every
        // other word crossed, "με" in "can you send με the report" is "me". The crossings must be this
        // line's own; a direction handed in with a single word (auto mode) says nothing about its neighbours.
        return _line.Anchors(token.Source.Script) == 0 && _line.CrossingsInto(direction) >= Quorum ? pulled : null;
    }
}
