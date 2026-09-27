namespace Lapsus.Sweep;

internal static class RuleC
{
    public static void Targets(Machine machine, FrequencyList en, FrequencyList target, int lines)
    {
        Report.Heading("Rule C — where the OS layout is asked to go");

        var random = new Random(20260911);
        var wrongLayoutWithout = 0;
        var wrongLayoutWith = 0;
        var finishedInEnglishWithout = 0;
        var finishedInEnglishWith = 0;

        for (var i = 0; i < lines; i++)
        {
            var foreign = machine.AsEnglishKeystrokes(string.Join(' ', target.SampleLine(random, 5, 8)));
            if (machine.Correct(foreign, phraseContext: false).TargetLayout == machine.Target.Layout)
                wrongLayoutWithout++;
            if (machine.Correct(foreign, phraseContext: true).TargetLayout == machine.Target.Layout)
                wrongLayoutWith++;

            var head = machine.AsEnglishKeystrokes(target.Sample(random));
            var tail = string.Join(' ', en.SampleLine(random, 4, 6));
            var mixed = $"{head} {tail}";

            if (machine.Correct(mixed, phraseContext: false).TargetLayout != machine.Target.Layout)
                finishedInEnglishWithout++;
            if (machine.Correct(mixed, phraseContext: true).TargetLayout != machine.Target.Layout)
                finishedInEnglishWith++;
        }

        Report.Row($"wrong-layout line asks for {machine.Target.Name}",
            (double)wrongLayoutWithout / lines, (double)wrongLayoutWith / lines, Better.Higher);
        Report.Row($"line finished in English does not ask for {machine.Target.Name}",
            (double)finishedInEnglishWithout / lines, (double)finishedInEnglishWith / lines, Better.Higher);
    }
}
