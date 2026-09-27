using Lapsus.Core.Layout;

namespace Lapsus.Sweep;

internal static class RuleD
{
    public static void TyposStayEnglish(Machine machine, FrequencyList en, int lines)
    {
        Report.Heading("Rule D — an English typo stays English");

        var random = new Random(20260913);
        var crossedAlone = 0;
        var fixedAlone = 0;
        var crossedWithout = 0;
        var crossedWith = 0;
        var examples = new List<string>();

        var measured = 0;
        while (measured < lines)
        {
            var word = en.Sample(random);
            if (word.Length < 4 || !word.All(char.IsAsciiLetterLower))
                continue;

            var typo = Mistype(word, random);
            measured++;

            var alone = machine.Correct(typo, phraseContext: true).Corrected;
            if (Scripts.Dominant(alone) == machine.Target.Script)
            {
                crossedAlone++;
                if (examples.Count < 8)
                    examples.Add($"{typo} → {alone}");
            }
            else if (alone == word)
            {
                fixedAlone++;
            }

            var around = en.SampleLine(random, 4, 6);
            var at = random.Next(around.Length + 1);
            if (Crossed(machine, around, typo, at, phraseContext: false)) crossedWithout++;
            if (Crossed(machine, around, typo, at, phraseContext: true)) crossedWith++;
        }

        Report.Value($"typo alone crossed into {machine.Target.ScriptName}", (double)crossedAlone / lines, Better.Lower);
        Report.Value("typo alone fixed back to its word", (double)fixedAlone / lines, Better.Higher);
        Report.Row($"typo in an English line crossed into {machine.Target.ScriptName}",
            (double)crossedWithout / lines, (double)crossedWith / lines, Better.Lower);

        foreach (var example in examples)
            Report.Line(string.Empty, example);
    }

    private static string Mistype(string word, Random random)
    {
        var at = random.Next(word.Length - 1);
        return random.Next(2) == 0
            ? string.Concat(word.AsSpan(0, at), word.AsSpan(at + 1, 1), word.AsSpan(at, 1), word.AsSpan(at + 2))
            : word.Remove(at, 1);
    }

    private static bool Crossed(Machine machine, string[] around, string typo, int at, bool phraseContext)
    {
        var words = new List<string>(around);
        words.Insert(at, typo);

        var corrected = Machine.Words(machine.Correct(string.Join(' ', words), phraseContext).Corrected);
        return at < corrected.Length && Scripts.Dominant(corrected[at]) == machine.Target.Script;
    }
}
