using Lapsus.Core.Layout;

namespace Lapsus.Sweep;

internal static class RuleB
{
    public static void Recall(Machine machine, FrequencyList target, int lines)
    {
        Report.Heading($"Rule B — a whole {machine.Target.Name} line typed on the English layout");

        var random = new Random(20260907);
        var wholeWithout = 0;
        var wholeWith = 0;
        var wordsWithout = 0;
        var wordsWith = 0;
        var words = 0;
        var examples = new List<string>();

        for (var i = 0; i < lines; i++)
        {
            var intended = string.Join(' ', target.SampleLine(random, 5, 8));
            var typed = machine.AsEnglishKeystrokes(intended);

            var without = machine.Correct(typed, phraseContext: false).Corrected;
            var with = machine.Correct(typed, phraseContext: true).Corrected;

            if (without == intended) wholeWithout++;
            if (with == intended) wholeWith++;
            else if (examples.Count < 8)
                examples.Add(Mismatch(intended, with));

            var expected = Machine.Words(intended);
            words += expected.Length;
            wordsWithout += Matching(expected, Machine.Words(without));
            wordsWith += Matching(expected, Machine.Words(with));
        }

        Report.Row("lines recovered in full", (double)wholeWithout / lines, (double)wholeWith / lines, Better.Higher);
        Report.Row("words recovered", (double)wordsWithout / words, (double)wordsWith / words, Better.Higher);

        foreach (var example in examples)
            Report.Line(string.Empty, example);

        Punctuated(machine, target, lines);
    }

    private static void Punctuated(Machine machine, FrequencyList target, int lines)
    {
        Report.Heading($"Rule B — the same {machine.Target.Name} line with a comma inside and a full stop at the end");

        var random = new Random(20260916);
        var wholeWithout = 0;
        var wholeWith = 0;
        var commaWithout = 0;
        var commaWith = 0;
        var examples = new List<string>();

        for (var i = 0; i < lines; i++)
        {
            var intendedWords = target.SampleLine(random, 5, 8);
            var commaAfter = random.Next(1, intendedWords.Length - 1);
            var intended = string.Join(' ', intendedWords.Select((w, n) => n == commaAfter ? w + "," : w)) + ".";
            var typed = string.Join(' ', intendedWords.Select((w, n) => machine.AsEnglishKeystrokes(w) + (n == commaAfter ? "," : ""))) + ".";

            var without = machine.Correct(typed, phraseContext: false).Corrected;
            var with = machine.Correct(typed, phraseContext: true).Corrected;

            if (without == intended) wholeWithout++;
            if (with == intended) wholeWith++;
            else if (examples.Count < 8)
                examples.Add(Mismatch(intended, with));

            if (Machine.Words(without).ElementAtOrDefault(commaAfter)?.EndsWith(',') == true) commaWithout++;
            if (Machine.Words(with).ElementAtOrDefault(commaAfter)?.EndsWith(',') == true) commaWith++;
        }

        Report.Row("lines recovered in full, punctuation kept", (double)wholeWithout / lines, (double)wholeWith / lines, Better.Higher);
        Report.Row("the comma survived as a comma", (double)commaWithout / lines, (double)commaWith / lines, Better.Higher);

        foreach (var example in examples)
            Report.Line(string.Empty, example);
    }

    private static string Mismatch(string intended, string got)
    {
        var wanted = Machine.Words(intended);
        var actual = Machine.Words(got);
        var parts = new List<string>();
        for (var i = 0; i < wanted.Length; i++)
        {
            var word = i < actual.Length ? actual[i] : "";
            if (word != wanted[i])
                parts.Add($"{wanted[i]} → {word}");
        }

        return wanted.Length == actual.Length ? string.Join(", ", parts) : $"{wanted.Length} words → {actual.Length}: {got}";
    }

    public static void FalsePositives(Machine machine, FrequencyList en, FrequencyList target, int lines)
    {
        Report.Heading($"Rule B — English words standing beside two wrong-layout {machine.Target.Name} ones");

        var random = new Random(20260908);
        var englishWords = 0;
        var lostWithout = 0;
        var lostWith = 0;
        var examples = new List<string>();

        for (var i = 0; i < lines; i++)
        {
            var english = en.SampleLine(random, 4, 6);
            var foreign = target.SampleLine(random, 2, 2).Select(machine.AsEnglishKeystrokes).ToArray();

            var typed = string.Join(' ', english.Concat(foreign));

            var without = Machine.Words(machine.Correct(typed, phraseContext: false).Corrected);
            var with = Machine.Words(machine.Correct(typed, phraseContext: true).Corrected);

            englishWords += english.Length;
            for (var w = 0; w < english.Length; w++)
            {
                if (w < without.Length && LeftLatin(without[w]))
                    lostWithout++;

                if (w >= with.Length || !LeftLatin(with[w]))
                    continue;

                lostWith++;
                if (examples.Count < 8)
                    examples.Add($"{english[w]} → {with[w]}   in “{typed}”");
            }
        }

        Report.Row($"English words pulled into {machine.Target.ScriptName}", (double)lostWithout / englishWords,
            (double)lostWith / englishWords, Better.Lower);

        foreach (var example in examples)
            Report.Line(string.Empty, example);
    }

    private static bool LeftLatin(string word)
    {
        return Scripts.Dominant(word) is { } script && script != Script.Latin;
    }

    private static int Matching(string[] expected, string[] actual)
    {
        var matched = 0;
        for (var i = 0; i < expected.Length && i < actual.Length; i++)
            if (expected[i] == actual[i])
                matched++;

        return matched;
    }
}
