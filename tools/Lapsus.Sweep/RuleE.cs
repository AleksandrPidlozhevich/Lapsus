using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Sweep;

internal static class RuleE
{
    private sealed record Variant(string Name, bool KeysOnly, bool Sticky);

    private static readonly Variant[] Variants =
    [
        new("alone", KeysOnly: false, Sticky: false),
        new("keys only", KeysOnly: true, Sticky: false),
        new("sticky", KeysOnly: false, Sticky: true),
        new("sticky keys only", KeysOnly: true, Sticky: true)
    ];

    public static void AutoMode(Machine machine, FrequencyList en, FrequencyList target, int lines, int minLength)
    {
        Report.Heading("Rule E — auto-after-space, word by word (" + string.Join(" → ", Variants.Select(v => v.Name)) + ")");

        var random = new Random(20260914);
        var targetWords = 0;
        var enWords = 0;
        var typoLines = 0;
        var targetRecovered = new int[Variants.Length];
        var enChanged = new int[Variants.Length];
        var typoCrossed = new int[Variants.Length];
        var examples = new List<string>[Variants.Length];
        for (var v = 0; v < Variants.Length; v++)
            examples[v] = [];

        var misses = new Dictionary<string, int>();

        for (var i = 0; i < lines; i++)
        {
            var intended = target.SampleLine(random, 4, 8);
            var typed = intended.Select(machine.AsEnglishKeystrokes).ToArray();
            targetWords += intended.Length;

            var english = en.SampleLine(random, 4, 8);
            enWords += english.Length;

            var word = en.Sample(random);
            var typo = word.Length >= 4 && word.All(char.IsAsciiLetterLower)
                ? word.Remove(random.Next(word.Length), 1)
                : null;
            if (typo is not null)
                typoLines++;

            for (var v = 0; v < Variants.Length; v++)
            {
                var recovered = Stream(machine, typed, Variants[v], minLength);
                targetRecovered[v] += Matches(recovered, intended);

                if (v == 0)
                    Classify(machine, typed, recovered, intended, misses, minLength);

                enChanged[v] += english.Length - Matches(Stream(machine, english, Variants[v], minLength), english);
                if (typo is null)
                    continue;

                var last = Stream(machine, [.. english, typo], Variants[v], minLength)[^1];
                if (Scripts.Dominant(last) != machine.Target.Script)
                    continue;

                typoCrossed[v]++;
                if (examples[v].Count < 8)
                    examples[v].Add($"{typo} → {last}");
            }
        }

        Report.Cells($"wrong-layout {machine.Target.Name} words recovered", Names, Rates(targetRecovered, targetWords), Better.Higher);
        Report.Cells("correct English words changed", Names, Rates(enChanged, enWords), Better.Lower);
        Report.Cells($"English typo at line end crossed into {machine.Target.ScriptName}", Names, Rates(typoCrossed, Math.Max(1, typoLines)), Better.Lower);

        TwoLetterChunks(machine, minLength);

        Report.Line("why \"alone\" missed a word", $"{targetWords - targetRecovered[0]} misses of {targetWords}");
        foreach (var (reason, count) in misses.OrderByDescending(m => m.Value))
            Report.Line(string.Empty, $"{count,7} ({(double)count / targetWords,6:P1})  {reason}");

        for (var v = 0; v < Variants.Length; v++)
            foreach (var example in examples[v])
                Report.Line(string.Empty, $"{Variants[v].Name}: {example}");
    }

    private static void Classify(
        Machine machine, string[] typed, string[] got, string[] intended, Dictionary<string, int> misses, int minLength)
    {
        for (var i = 0; i < intended.Length && i < got.Length; i++)
        {
            if (got[i] == intended[i])
                continue;

            var known = machine.Knows(intended[i]);
            var stem = !known && machine.KnowsStem(intended[i]);
            var leftAsTyped = got[i] == typed[i];

            var reason = !AutoCorrectPolicy.IsEligible(typed[i], false, minLength)
                ? $"auto mode would not touch the chunk ({Letters(intended[i])} letters, or digits and capitals)"
                : known
                    ? leftAsTyped ? "in the list, left as typed" : "in the list, another reading won"
                    : stem
                        ? leftAsTyped ? "one clitic from the list, left as typed" : "one clitic from the list, another reading won"
                        : leftAsTyped ? "not in the list at all, left as typed" : "not in the list at all, another reading won";

            misses[reason] = misses.GetValueOrDefault(reason) + 1;
        }
    }

    private static void TwoLetterChunks(Machine machine, int minLength)
    {
        var chunks = 0;
        var rewritten = 0;
        var examples = new List<string>();

        for (var a = 'a'; a <= 'z'; a++)
        for (var b = 'a'; b <= 'z'; b++)
        {
            var chunk = $"{a}{b}";
            if (machine.KnowsEnglish(chunk))
                continue;

            chunks++;
            if (!AutoCorrectPolicy.IsEligible(chunk, false, minLength))
                continue;

            var verdict = machine.Correct(chunk, phraseContext: true, hints: new CorrectionHints(null, true));
            if (!verdict.Changed || Scripts.Dominant(verdict.Corrected) != machine.Target.Script)
                continue;

            rewritten++;
            if (examples.Count < 8)
                examples.Add($"{chunk} → {verdict.Corrected}");
        }

        Report.Value(
            $"two-letter chunks English does not use, sent to {machine.Target.ScriptName} ({chunks} of 676)",
            (double)rewritten / Math.Max(1, chunks), Better.Lower);

        foreach (var example in examples)
            Report.Line(string.Empty, example);
    }

    private static int Letters(string word)
    {
        return word.Count(char.IsLetter);
    }

    private static readonly string[] Names = Variants.Select(v => v.Name).ToArray();

    private static double[] Rates(int[] counts, int total)
    {
        return counts.Select(c => (double)c / total).ToArray();
    }

    private static string[] Stream(Machine machine, string[] typed, Variant variant, int minLength)
    {
        var line = new List<string>(typed.Length);
        Script? direction = null;
        foreach (var chunk in typed)
        {
            line.Add(chunk);
            if (!AutoCorrectPolicy.IsEligible(chunk, direction is not null, minLength))
                continue;

            var verdict = machine.Correct(
                chunk, phraseContext: true, hints: new CorrectionHints(direction, variant.KeysOnly));
            line[^1] = verdict.Corrected;

            if (!variant.Sticky)
                continue;

            if (verdict.Changed)
            {
                if (Scripts.Dominant(verdict.Corrected) is { } into && into != Scripts.Dominant(chunk))
                    direction = into;
            }
            else if (verdict.Settled is { Count: > 0 } settled && TextSpans.CoversEveryLetter(chunk, settled))
            {
                direction = null;
            }
        }

        return [.. line];
    }

    private static int Matches(string[] got, string[] expected)
    {
        var same = 0;
        for (var i = 0; i < expected.Length && i < got.Length; i++)
            if (got[i] == expected[i])
                same++;

        return same;
    }
}
