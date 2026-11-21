using System.Diagnostics;
using Lapsus.Core.Models;
using Lapsus.Core.Speech;
using Lapsus.Neural;

// Experiment: TermRestorer.Guesses judged by the neural brain's model, on an eval log's raw/want pairs.
internal static class TermGuessRun
{
    public static int Run(string modelDir, string dictDir, string logPath, string lang)
    {
        var spell = Eval.LoadDictionariesFor(dictDir);
        var restorer = new TermRestorer(spell);
        var declared = File.Exists(Path.Combine(modelDir, GenAiPack.ConfigFile))
            ? GenAiPack.ReadProviders(File.ReadAllText(Path.Combine(modelDir, GenAiPack.ConfigFile)))
            : [];
        var plan = ExecutionPlan.Build(ComputeDevicePreference.Auto, ComputeDeviceProbe.Capabilities(),
            GenAiPack.InferDevice("", declared), declared);
        using var llm = new OnnxGenAiLlm();
        llm.Load(modelDir, plan);
        llm.WarmUpAsync().GetAwaiter().GetResult();
        var ranker = new DictationTermRanker(llm);
        Console.WriteLine($"{Path.GetFileName(modelDir)} on {llm.ExecutionProvider}");

        var lines = File.ReadAllLines(logPath);
        var before = new Eval.Score();
        var after = new Eval.Score();
        var clock = new Stopwatch();
        double spent = 0;
        int asked = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("  raw      ")) continue;
            var raw = lines[i][11..];
            var want = Enumerable.Range(i + 1, 3).Where(k => k < lines.Length && lines[k].StartsWith("  want     "))
                .Select(k => lines[k][11..]).FirstOrDefault();
            if (want is null) continue;
            var restored = restorer.Restore(raw, lang);
            var guesses = restorer.Guesses(restored, lang);
            clock.Restart();
            var ranked = ranker.RankAsync(restored, guesses, CancellationToken.None).GetAwaiter().GetResult();
            spent += clock.ElapsedMilliseconds;
            asked += guesses.Count;
            before.Add(want, restored);
            after.Add(want, ranked);
            if (guesses.Count > 0)
                Console.WriteLine($"{string.Join(", ", guesses.Select(g => restored.Substring(g.Start, g.Length) + "→" + g.Term))}\n  {restored}\n  {ranked}\n  want {want}  ({clock.ElapsedMilliseconds} ms)");
        }
        Console.WriteLine($"restored {before}\nranked   {after}\n{asked} guesses, {spent:F0} ms in all");
        return 0;
    }
}
