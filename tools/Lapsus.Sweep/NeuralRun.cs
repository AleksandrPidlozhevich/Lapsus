using System.Diagnostics;
using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Models;
using Lapsus.Neural;

namespace Lapsus.Sweep;

internal sealed record LoadedModel(OnnxGenAiLlm Llm, string Name, string Device);

// The neural brain as the app builds it — the model with the dictionary brain riding along as adviser —
// measured on the same lists. Every number is shown as "dictionary alone → neural", so the question each
// row answers is whether loading a model was worth it.
internal static class NeuralRun
{
    public static LoadedModel Load(string modelDirectory, ComputeDevicePreference preference)
    {
        var declared = File.Exists(Path.Combine(modelDirectory, GenAiPack.ConfigFile))
            ? GenAiPack.ReadProviders(File.ReadAllText(Path.Combine(modelDirectory, GenAiPack.ConfigFile)))
            : [];
        var plan = ExecutionPlan.Build(
            preference, ComputeDeviceProbe.Capabilities(), GenAiPack.InferDevice("", declared), declared);

        var llm = new OnnxGenAiLlm();
        llm.Load(modelDirectory, plan);

        // The first generation pays for graph setup; keep it out of the latency figures.
        llm.CompleteAsync("Reply with the word.", "ok", 2).GetAwaiter().GetResult();

        return new LoadedModel(llm, Path.GetFileName(Path.TrimEndingDirectorySeparator(modelDirectory)), llm.ExecutionProvider);
    }

    public static void Measure(Machine machine, ILocalLlm llm, FrequencyList en, FrequencyList target, int lines)
    {
        var recorder = new RecordingLlm(llm);
        var brain = new NeuralPhraseRewriter(recorder, machine.Brain);
        var run = new Run(machine, brain, recorder);
        var name = machine.Target.Name;

        var random = new Random(20260929);
        run.Measure($"Neural — a {name} line typed on the English layout", lines, () =>
        {
            var intended = string.Join(' ', target.SampleLine(random, 2, 5));
            return new Case(machine.AsEnglishKeystrokes(intended), intended, Machine.En);
        });

        random = new Random(20260930);
        run.Measure($"Neural — one {name} word typed on the English layout", lines, () =>
        {
            var intended = target.Sample(random);
            return new Case(machine.AsEnglishKeystrokes(intended), intended, Machine.En);
        });

        random = new Random(20261001);
        run.Measure($"Neural — an English line typed on the {name} layout", lines, () =>
        {
            var intended = string.Join(' ', en.SampleLine(random, 2, 5));
            return new Case(machine.AsTargetKeystrokes(intended), intended, machine.TargetSource);
        });

        random = new Random(20261002);
        run.Measure($"Neural — a correct {name} line, to be left alone", lines, () =>
        {
            var line = string.Join(' ', target.SampleLine(random, 2, 5));
            return new Case(line, line, machine.TargetSource);
        });

        random = new Random(20261003);
        run.Measure("Neural — a correct English line, to be left alone", lines, () =>
        {
            var line = string.Join(' ', en.SampleLine(random, 2, 5));
            return new Case(line, line, Machine.En);
        });

        var fixtures = Fixtures(machine).ToList();
        foreach (var (status, rows) in fixtures.GroupBy(f => f.Status).Select(g => (g.Key, g.ToList())))
        {
            var next = 0;
            run.Measure($"Neural — typing fixtures marked {status}", rows.Count, () => rows[next++].Case);
        }

        run.Latency();
    }

    private sealed class Run(Machine machine, NeuralPhraseRewriter brain, RecordingLlm recorder)
    {
        public void Measure(string heading, int count, Func<Case> next)
        {
            Report.Heading(heading);

            var dictionaryRight = 0;
            var neuralRight = 0;
            var modelRight = 0;
            var modelCalls = 0;
            var sources = new Dictionary<string, int>();
            var examples = new List<string>();

            for (var i = 0; i < count; i++)
            {
                var (typed, expected, active) = next();

                var alone = machine.Brain.CorrectPhrase(typed, active, machine.Installed, machine.Candidates).Corrected;

                recorder.Reply = null;
                var got = brain.CorrectPhrase(typed, active, machine.Installed, machine.Candidates).Corrected;
                var reply = recorder.Reply is { } raw ? NeuralPhraseRewriter.SanitizeModelOutput(raw, typed) : null;

                if (alone == expected) dictionaryRight++;
                if (got == expected) neuralRight++;
                if (reply is not null)
                {
                    modelCalls++;
                    if (reply == expected.Trim()) modelRight++;
                }

                var source = SourceOf(typed, got, alone, reply);
                sources[source] = sources.GetValueOrDefault(source) + 1;

                if (got != expected && examples.Count < 8)
                    examples.Add($"{typed} → {got}   wanted {expected}   ({source}{(reply is null || reply == got.Trim() ? "" : $"; model said “{reply}”")})");
            }

            if (count == 0)
                return;

            Report.Row("right (dictionary alone → neural)", (double)dictionaryRight / count, (double)neuralRight / count, Better.Higher);
            if (modelCalls > 0)
                Report.Value($"model's own answer right, of {modelCalls} asked", (double)modelRight / modelCalls, Better.Higher);

            var order = new[] { Source.Model, Source.Adviser, Source.Remap, Source.AsTyped, Source.Shortcut };
            Report.Cells("whose answer went out", order,
                order.Select(s => (double)sources.GetValueOrDefault(s) / count).ToList(), Better.Either);

            foreach (var example in examples)
                Report.Line(string.Empty, example);
        }

        private static string SourceOf(string typed, string got, string alone, string? reply)
        {
            if (got == typed)
                return Source.AsTyped;
            if (reply is null)
                return Source.Shortcut;
            if (got.Trim() == reply)
                return Source.Model;
            return got == alone ? Source.Adviser : Source.Remap;
        }

        public void Latency()
        {
            var times = recorder.Elapsed.Order().ToList();
            Report.Heading("Neural — time per model call");
            if (times.Count == 0)
            {
                Report.Line("no model calls", string.Empty);
                return;
            }

            // Printed, not recorded: milliseconds depend on the machine, not on the change under test.
            Report.Line("calls", times.Count.ToString());
            Report.Line("median / p95 / max",
                $"{Percentile(times, 0.5):F0} / {Percentile(times, 0.95):F0} / {times[^1]:F0} ms");
            recorder.Elapsed.Clear();
        }

        private static double Percentile(List<double> sorted, double p)
        {
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
        }
    }

    private static class Source
    {
        public const string Model = "model";
        public const string Adviser = "dictionary";
        public const string Remap = "remap";
        public const string AsTyped = "as typed";
        public const string Shortcut = "no model call";
    }

    private readonly record struct Case(string Typed, string Expected, LayoutSource Active);

    private readonly record struct Fixture(Case Case, string Status);

    // Lapsus.Core.Tests/Languages/Fixtures — the hand-picked hard cases, pending rows included:
    // those are the ones the dictionary brain still gets wrong.
    private static IEnumerable<Fixture> Fixtures(Machine machine)
    {
        var key = machine.Target.Key == "bg-phonetic" ? "bg" : machine.Target.Key;
        var path = Path.Combine(Baseline.RepositoryRoot() ?? ".", "Lapsus.Core.Tests", "Languages", "Fixtures", $"{key}.cases.tsv");
        if (!File.Exists(path))
            yield break;

        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var fields = line.Split('\t');
            if (fields.Length < 3)
                continue;

            var active = Scripts.Dominant(fields[0]) == machine.Target.Script ? machine.TargetSource : Machine.En;
            yield return new Fixture(new Case(fields[0], fields[1], active), fields[2]);
        }
    }

    private sealed class RecordingLlm(ILocalLlm inner) : ILocalLlm
    {
        public string? Reply { get; set; }

        public List<double> Elapsed { get; } = [];

        public bool IsLoaded => inner.IsLoaded;

        public void Unload() => inner.Unload();

        public async Task<string> CompleteAsync(
            string systemPrompt, string userText, int answerChars, CancellationToken cancellationToken = default)
        {
            var clock = Stopwatch.StartNew();
            var reply = await inner.CompleteAsync(systemPrompt, userText, answerChars, cancellationToken).ConfigureAwait(false);
            Elapsed.Add(clock.Elapsed.TotalMilliseconds);
            Reply = reply;
            return reply;
        }

        public void Dispose()
        {
        }
    }
}
