using Lapsus.Core.Layout;

namespace Lapsus.Sweep;

internal static class RuleA
{
    public static void Recall(
        Machine machine, FrequencyList en, IReadOnlyList<FrequencyWord> heldOut, int lines)
    {
        Report.Heading("Rule A — one out-of-vocabulary word inside a correct English line");

        var random = new Random(20260909);
        var pulledWithout = 0;
        var pulledWith = 0;
        var fixedWith = 0;
        var knownWithout = 0;
        var knownWith = 0;

        for (var i = 0; i < lines; i++)
        {
            var around = en.SampleLine(random, 4, 6);
            var unknown = heldOut[random.Next(heldOut.Count)].Word;
            var at = random.Next(around.Length + 1);

            if (WhatBecameOfIt(machine, around, unknown, at, false, out _) == Fate.PulledAcross)
                pulledWithout++;

            switch (WhatBecameOfIt(machine, around, unknown, at, true, out _))
            {
                case Fate.PulledAcross: pulledWith++; break;
                case Fate.FixedInPlace: fixedWith++; break;
            }

            var known = en.Sample(random);
            if (Survives(machine, around, known, at, phraseContext: false)) knownWithout++;
            if (Survives(machine, around, known, at, phraseContext: true)) knownWith++;
        }

        Report.Row($"pulled across into {machine.Target.ScriptName}", (double)pulledWithout / lines, (double)pulledWith / lines, Better.Lower);
        Report.Value("spell-fixed in place, still English", (double)fixedWith / lines, Better.Either);
        Report.Row("control: known word left alone", (double)knownWithout / lines, (double)knownWith / lines, Better.Higher);
    }

    public static void RecallInMixedLine(
        Machine machine, FrequencyList target, IReadOnlyList<FrequencyWord> heldOut, int lines)
    {
        Report.Heading($"Rule A — one out-of-vocabulary English word inside a correct {machine.Target.Name} line");

        var random = new Random(20260912);
        var pulledWithout = 0;
        var pulledWith = 0;
        var fixedWithout = 0;
        var fixedWith = 0;
        var examples = new List<string>();

        for (var i = 0; i < lines; i++)
        {
            var around = target.SampleLine(random, 4, 6);
            var unknown = heldOut[random.Next(heldOut.Count)].Word;
            var at = random.Next(around.Length + 1);

            switch (WhatBecameOfIt(machine, around, unknown, at, false, out _))
            {
                case Fate.PulledAcross: pulledWithout++; break;
                case Fate.FixedInPlace: fixedWithout++; break;
            }

            switch (WhatBecameOfIt(machine, around, unknown, at, true, out var got))
            {
                case Fate.PulledAcross:
                    pulledWith++;
                    if (examples.Count < 6)
                        examples.Add($"{unknown} → {got}");
                    break;
                case Fate.FixedInPlace:
                    fixedWith++;
                    break;
            }
        }

        Report.Row($"pulled across into {machine.Target.ScriptName}", (double)pulledWithout / lines, (double)pulledWith / lines, Better.Lower);
        Report.Row("spell-fixed in place, still English",
            (double)fixedWithout / lines, (double)fixedWith / lines, Better.Either);

        foreach (var example in examples)
            Report.Line(string.Empty, $"still pulled: {example}");
    }

    public static void FalsePositives(Machine machine, FrequencyList target, int lines)
    {
        Report.Heading($"Rule A — a typo'd word inside a wholly wrong-layout {machine.Target.Name} line");

        var random = new Random(20260910);
        var repairedWithout = 0;
        var repairedWith = 0;
        var intendedWithout = 0;
        var intendedWith = 0;
        var reverted = new List<string>();

        for (var i = 0; i < lines; i++)
        {
            var intended = target.SampleLine(random, 4, 6);
            var typed = intended.Select(machine.AsEnglishKeystrokes).ToArray();

            var at = random.Next(typed.Length);
            if (typed[at].Length < 4)
                continue;

            var slip = random.Next(typed[at].Length);
            var mistyped = typed[at].Remove(slip, 1);
            var line = string.Join(' ', typed.Select((w, n) => n == at ? mistyped : w));

            var without = Machine.Words(machine.Correct(line, phraseContext: false).Corrected);
            var with = Machine.Words(machine.Correct(line, phraseContext: true).Corrected);

            var wasRepaired = at < without.Length && Scripts.Dominant(without[at]) == machine.Target.Script;
            var stillRepaired = at < with.Length && Scripts.Dominant(with[at]) == machine.Target.Script;

            if (wasRepaired) repairedWithout++;
            if (stillRepaired) repairedWith++;

            if (at < without.Length && without[at] == intended[at]) intendedWithout++;
            if (at < with.Length && with[at] == intended[at]) intendedWith++;

            if (wasRepaired && !stillRepaired && reverted.Count < 8)
                reverted.Add($"{without[at]} → {with[at]}   in “{line}”");
        }

        Report.Row($"typo'd word still crossed into {machine.Target.ScriptName}",
            (double)repairedWithout / lines, (double)repairedWith / lines, Better.Higher);
        Report.Row("and came back as the word that was meant",
            (double)intendedWithout / lines, (double)intendedWith / lines, Better.Higher);

        foreach (var example in reverted)
            Report.Line(string.Empty, example);
    }

    private enum Fate
    {
        Kept,
        FixedInPlace,
        PulledAcross
    }

    private static Fate WhatBecameOfIt(
        Machine machine, string[] around, string word, int at, bool phraseContext, out string got)
    {
        var words = new List<string>(around);
        words.Insert(at, word);
        var line = string.Join(' ', words);

        var corrected = Machine.Words(machine.Correct(line, phraseContext).Corrected);
        got = at < corrected.Length ? corrected[at] : string.Empty;

        if (got == word)
            return Fate.Kept;

        return Scripts.Dominant(got) == Scripts.Dominant(word) ? Fate.FixedInPlace : Fate.PulledAcross;
    }

    private static bool Survives(
        Machine machine, string[] around, string word, int at, bool phraseContext)
    {
        return WhatBecameOfIt(machine, around, word, at, phraseContext, out _) == Fate.Kept;
    }
}
