using Lapsus.Core.Layout;

namespace Lapsus.Sweep;

internal static class RuleF
{
    public static void HeldOutWords(
        Machine machine, FrequencyList known, IReadOnlyList<FrequencyWord> heldOut, int lines)
    {
        Report.Heading($"Rule F — a wrong-layout {machine.Target.Name} word the list does not have");

        var random = new Random(20260918);
        var alone = 0;
        var inLine = 0;
        var total = 0;
        var examples = new List<string>();
        var misses = new Dictionary<string, int>();

        for (var i = 0; i < lines; i++)
        {
            var word = heldOut[random.Next(heldOut.Count)].Word;
            var typed = machine.AsEnglishKeystrokes(word);

            if (typed == word)
                continue;

            total++;

            var byItself = machine.Correct(typed, phraseContext: true).Corrected;
            if (byItself == word)
                alone++;
            else
                Classify(machine, typed, byItself, misses);

            var line = known.SampleLine(random, 3, 5).Select(machine.AsEnglishKeystrokes).Append(typed);
            var corrected = Machine.Words(machine.Correct(string.Join(' ', line), phraseContext: true).Corrected);
            if (corrected.Length > 0 && corrected[^1] == word)
                inLine++;
            else if (examples.Count < 6)
                examples.Add($"{typed} → {(corrected.Length > 0 ? corrected[^1] : string.Empty)}   (wanted {word})");
        }

        total = Math.Max(1, total);
        Report.Value("the word on its own, recovered", (double)alone / total, Better.Higher);
        Report.Value("the word ending a line of its language, recovered", (double)inLine / total, Better.Higher);

        Report.Line("what became of it instead", $"{total - alone} of {total} on their own");
        foreach (var (reason, count) in misses.OrderByDescending(m => m.Value))
            Report.Line(string.Empty, $"{count,7} ({(double)count / total,6:P1})  {reason}");

        foreach (var example in examples)
            Report.Line(string.Empty, example);
    }

    private static void Classify(Machine machine, string typed, string got, Dictionary<string, int> misses)
    {
        var script = Scripts.Dominant(got);
        var reason = got == typed
            ? "left on the English keys as typed"
            : script == machine.Target.Script
                ? "crossed, but came back another word of the language"
                : machine.KnowsEnglish(got)
                    ? "spell-fixed into an English word instead"
                    : "changed into something else";

        misses[reason] = misses.GetValueOrDefault(reason) + 1;
    }
}
