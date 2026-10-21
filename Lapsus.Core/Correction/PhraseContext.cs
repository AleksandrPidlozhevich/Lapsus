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

        for (var i = 0; i < _tokens.Count; i++)
        {
            if (IsTermAmongAnotherLanguage(i))
                _tokens[i] = _tokens[i].Reverted();
            else if (SameScriptLayoutInstead(i) is { } sibling)
                _tokens[i] = _tokens[i] with { Chosen = sibling, ChosenScript = _tokens[i].Source.Script };
        }

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
        else if (HeldScript() is { } held)
            FollowNeighbours(held, false, listedOnly: true);

        FollowRuns();

        return _tokens;
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
            for (var i = 0; i < _tokens.Count; i++)
            {
                if (FollowsItsRun(i) is not { } follow)
                    continue;

                _tokens[i] = _tokens[i] with { Chosen = follow.Pulled, ChosenScript = follow.Into };
                moved = true;
            }
        } while (moved);
    }

    private (ScoredWord Pulled, Script Into)? FollowsItsRun(int index)
    {
        if (!IsShortAndUnmoved(index) || _tokens[index].BaselineFrequency <= 0 || !StandsAlone(index))
            return null;

        var token = _tokens[index];
        var left = WordBeside(index, -1);
        var right = WordBeside(index, 1);
        var fromLeft = CrossedOnTheSameKeys(left, token);
        var fromRight = CrossedOnTheSameKeys(right, token);
        if (fromLeft is not null && fromRight is not null && fromLeft != fromRight)
            return null;

        if ((fromLeft ?? fromRight) is not { } into)
            return OnlyShortWordsBeside(index);

        if (IsBlindTo(token, into) ||
            readInScript(token.Typed, token.Source, into) is not { } pulled || !WordScorer.IsDictionaryHit(pulled.Score))
            return null;

        // The commoner word must not give way: "in" next to a crossing is still "in", never шт.
        var lead = scorer.FrequencyOf(pulled.Text, into) - token.BaselineFrequency;
        if (lead < 0)
            return null;

        // At a seam between a real word of its own and the crossing, only a far commoner reading moves:
        // "oh, ye 'nj fix" is "oh, ну это fix", ye being a rare word and ну a common one.
        var seam = IsOwnWordBeside(left, token, into) || IsOwnWordBeside(right, token, into);
        return seam && lead < MinFrequencyLeadWithoutANeighbour ? null : (pulled, into);
    }

    // "ye jr, давай": a run of short words, every one of them both languages' and next to a word typed right in
    // the other script, in a line with no real word of their own. They go to that script when far commoner
    // there. A single short word stays as typed: "Спартак vs ЦСКА".
    private (ScoredWord Pulled, Script Into)? OnlyShortWordsBeside(int index)
    {
        var token = _tokens[index];
        var first = index;
        while (WordBeside(first, -1) is { } before && _tokens[before].Source.Script == token.Source.Script)
            first = before;

        var last = index;
        while (WordBeside(last, 1) is { } after && _tokens[after].Source.Script == token.Source.Script)
            last = after;

        var run = 0;
        for (var i = first; i <= last; i++)
        {
            if (!_tokens[i].IsWord)
                continue;

            if (!IsShortAndUnmoved(i))
                return null;
            run++;
        }

        if (run < Quorum || Anchors(token.Source.Script) > 0)
            return null;

        if ((NativeScriptOf(WordBeside(first, -1)) ?? NativeScriptOf(WordBeside(last, 1))) is not { } into ||
            into == token.Source.Script || IsBlindTo(token, into))
            return null;

        if (readInScript(token.Typed, token.Source, into) is not { } pulled || !WordScorer.IsDictionaryHit(pulled.Score))
            return null;

        return scorer.FrequencyOf(pulled.Text, into) - token.BaselineFrequency >= MinFrequencyLeadWithoutANeighbour
            ? (pulled, into)
            : null;
    }

    private bool IsShortAndUnmoved(int index)
    {
        var token = _tokens[index];
        return token.IsWord && !token.Chosen.Changed && !token.IsException &&
               WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length < MinAnchorLetters;
    }

    private int? WordBeside(int index, int step)
    {
        for (var i = index + step; i >= 0 && i < _tokens.Count; i += step)
            if (_tokens[i].IsWord)
                return i;

        return null;
    }

    // The script a neighbour typed on the same layout crossed into.
    private Script? CrossedOnTheSameKeys(int? index, in TokenReading token)
    {
        if (index is not { } i)
            return null;

        var other = _tokens[i];
        return other.Source.Script == token.Source.Script && other.Chosen.Changed &&
               other.ChosenScript is { } into && into != other.Source.Script &&
               other.Edits < MinEditsToNeedANeighbour && WordScorer.IsDictionaryHit(other.Chosen.Score)
            ? into
            : null;
    }

    // A real word of the token's own script that the other script cannot read: "oh", "fix".
    private bool IsOwnWordBeside(int? index, in TokenReading token, Script into)
    {
        if (index is not { } i)
            return false;

        var other = _tokens[i];
        if (!IsNativeWord(other, token.Source.Script))
            return false;

        return WordScanner.TrimToLetters(other.Typed, other.Source.Script).Length >= MinAnchorLetters ||
               readInScript(other.Typed, other.Source, into) is not { } reading ||
               !WordScorer.IsDictionaryHit(reading.Score);
    }

    private Script? NativeScriptOf(int? index)
    {
        return index is { } i && IsNativeWord(_tokens[i], _tokens[i].Source.Script) ? _tokens[i].Source.Script : null;
    }

    // The one script whose real words hold a line nothing crossed out of. A word of another layout that
    // slipped in spells a rarer word there ("πες μου pew όταν είσαι έτοιμος"), so nothing crossed on its
    // own; it goes with its neighbours like any word both languages know.
    private Script? HeldScript()
    {
        Script? held = null;
        var seen = new List<Script>();
        foreach (var token in _tokens)
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

            // A word typed right in that language says the writer is writing it: "ну да, это был deploy yf
            // prod" is на, not "of".
            if (IsNativeWord(other, into))
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
        var token = _tokens[index];
        if (!token.IsWord || !token.Chosen.Changed || token.Edits == 0 || token.ChosenScript != token.Source.Script)
            return false;

        var others = 0;
        for (var i = 0; i < _tokens.Count; i++)
        {
            var other = _tokens[i];
            if (i == index || !other.IsWord)
                continue;

            // Anything else left in this script makes it a line of this language, where a typo is a typo.
            if (other.Source.Script == token.Source.Script && other.ChosenScript == token.Source.Script)
                return false;

            if (other.Source.Script != token.Source.Script && IsNativeWord(other, other.Source.Script))
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
        var token = _tokens[index];
        if (!token.IsWord || !token.Chosen.Changed || token.ChosenScript is not { } into || into == token.Source.Script ||
            !WordScorer.IsDictionaryHit(scorer.Score(token.Chosen.Text, into)))
            return null;

        var own = 0;
        for (var i = 0; i < _tokens.Count; i++)
        {
            if (i == index || !_tokens[i].IsWord)
                continue;

            if ((Crossed(_tokens[i]) && _tokens[i].ChosenScript == into) || IsNativeWord(_tokens[i], into))
                return null;

            if (IsNativeWord(_tokens[i], token.Source.Script))
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
    // and two words in that language settle the line for the short words.
    private void FollowNeighbours(Script into, bool settled, bool listedOnly = false)
    {
        bool moved;
        do
        {
            moved = false;
            for (var i = 0; i < _tokens.Count; i++)
            {
                if (listedOnly && IsKnownWithoutACount(_tokens[i]))
                    continue;

                if (FollowsNeighbours(i, into, settled || CrossingsInto(into) >= Quorum) is not { } pulled)
                    continue;

                _tokens[i] = _tokens[i] with { Chosen = pulled, ChosenScript = into };
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
        var isShort = WordScanner.TrimToLetters(token.Typed, token.Source.Script).Length < MinAnchorLetters;

        // Words typed right in that language settle the line too, but only for a plain word both lists know
        // ("lf" is да). One its own list lacks, or an abbreviation ("m.o.", "t0"), was typed so on purpose.
        settled = settled || (token.BaselineFrequency > 0 && StandsAlone(index) && IsWrittenIn(into));

        // Among short words only ("ой, nf ye"), a line already written in that language takes them along.
        if (isShort && settled && left is null && right is null)
            return lead >= 0 ? pulled : null;

        // With no neighbour in that language only a long word moves, on frequency alone: για over "gia". A short
        // one typed right stays: "ну vs code" keeps its "vs" though мы is far commoner.
        if (left != into && right != into)
            return !isShort && lead >= MinFrequencyLeadWithoutANeighbour ? pulled : null;

        // Beside one crossing a short word is no line at all ("ghbdtn d"). At the seam it goes with the
        // language the line is written in: "στείλε μου to link" is Greek, "strength to αλήθεια" English.
        if (isShort && (!settled || (left != right && !HoldsTheLine(into, token.Source.Script))))
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

    // A word on its own between spaces, sentence punctuation after it allowed: "lf," yes; the letters of
    // "m.o." or "t0" no, those are an abbreviation typed so on purpose.
    private bool StandsAlone(int index)
    {
        if (index > 0 && _tokens[index - 1] is var before && (before.IsWord || !EndsInSpace(before.Typed)))
            return false;

        if (index + 1 >= _tokens.Count)
            return true;

        var after = _tokens[index + 1];
        if (after.IsWord)
            return false;

        var rest = after.Typed.AsSpan().TrimStart(".,!?:;");
        return rest.IsEmpty ? index + 2 >= _tokens.Count : char.IsWhiteSpace(rest[0]);
    }

    private static bool EndsInSpace(string text)
    {
        return text.Length > 0 && char.IsWhiteSpace(text[^1]);
    }

    // A word typed in its own script and listed there, however short: "ну", "да" typed on the Russian layout.
    // Unlike a crossing it is no coincidence of keys, so it says which language the writer is in.
    private static bool IsNativeWord(in TokenReading token, Script script)
    {
        return token.IsWord && !token.Chosen.Changed && token.Source.Script == script &&
               WordScorer.IsDictionaryHit(token.Baseline);
    }

    // Two words in that language, crossed into it or typed in it: "ой, ye dct" is Russian (ой, все), and its
    // "ye" is ну. Crossings alone left the forgotten interjections of a mixed line behind.
    private bool IsWrittenIn(Script script)
    {
        var words = 0;
        foreach (var token in _tokens)
            if ((Crossed(token) && token.ChosenScript == script) || IsNativeWord(token, script))
                words++;

        return words >= Quorum;
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
        var sandwiched = SideOf(index, -1) == direction && SideOf(index, 1) == direction;
        if (PullsInAnywhere(token, direction, sandwiched) is not { } pulled)
            return null;

        // Between two real words of its own script the line's direction is not enough: "let me know πότε".
        var own = token.Source.Script;
        return SideOf(index, -1) == own && SideOf(index, 1) == own &&
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
            Anchors(token.Source.Script) is var anchors and >= Quorum && anchors > CrossingsInto(direction))
            return null;

        if (IsBlindTo(token, direction))
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
        return Anchors(token.Source.Script) == 0 && CrossingsInto(direction) >= Quorum ? pulled : null;
    }
}
