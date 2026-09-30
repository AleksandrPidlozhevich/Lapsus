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

    public static void Measure(
        Machine machine, ILocalLlm llm, FrequencyList en, FrequencyList target, int lines, string? blocks = null)
    {
        var recorder = new RecordingLlm(llm);
        var brain = new NeuralPhraseRewriter(recorder, machine.Brain);
        var run = new Run(machine, brain, recorder, blocks);
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

        random = new Random(20261004);
        run.Measure($"Neural — a {name} line with one typo", lines, () =>
        {
            var words = target.SampleLine(random, 3, 6);
            var (typed, slip) = WithTypo(random, words, machine.Target.Map, machine.Knows);
            return new Case(typed, string.Join(' ', words), machine.TargetSource, slip);
        });

        random = new Random(20261005);
        run.Measure("Neural — an English line with one typo", lines, () =>
        {
            var words = en.SampleLine(random, 3, 6);
            var (typed, slip) = WithTypo(random, words, BundledKeyboardMaps.En, machine.KnowsEnglish);
            return new Case(typed, string.Join(' ', words), Machine.En, slip);
        });

        // Real sentences, where the model has context to read — the frequency-list lines above are words
        // drawn at random, and no reader can tell "whn" meant "when" among them.
        var own = Sentences(machine.Target.Code);
        var english = Sentences("en");
        if (own.Count > 0)
        {
            random = new Random(20261006);
            var at = 0;
            run.Measure($"Neural — a natural {name} sentence with one typo", Math.Min(lines, own.Count * 3), () =>
            {
                var words = Machine.Words(own[at++ % own.Count]);
                var (typed, slip) = WithTypo(random, words, machine.Target.Map, machine.Knows);
                return new Case(typed, string.Join(' ', words), machine.TargetSource, slip);
            });

            var kept = 0;
            run.Measure($"Neural — a natural {name} sentence, to be left alone", own.Count, () =>
            {
                var sentence = own[kept++];
                return new Case(sentence, sentence, machine.TargetSource);
            });

            // Only what the layout can type in full; a hamza form it lacks would stay Arabic in the keys.
            var typeable = own.Where(sentence => Machine.Words(sentence).All(machine.Target.CanType)).ToList();
            var next = 0;
            run.Measure($"Neural — a natural {name} sentence typed on the English layout", typeable.Count, () =>
            {
                var sentence = typeable[next++];
                return new Case(machine.AsEnglishKeystrokes(sentence), sentence, Machine.En);
            });
        }

        if (english.Count > 0)
        {
            random = new Random(20261007);
            var at = 0;
            run.Measure("Neural — a natural English sentence with one typo", Math.Min(lines, english.Count * 3), () =>
            {
                var words = Machine.Words(english[at++ % english.Count]);
                var (typed, slip) = WithTypo(random, words, BundledKeyboardMaps.En, machine.KnowsEnglish);
                return new Case(typed, string.Join(' ', words), Machine.En, slip);
            });

            var kept = 0;
            run.Measure("Neural — a natural English sentence, to be left alone", english.Count, () =>
            {
                var sentence = english[kept++];
                return new Case(sentence, sentence, Machine.En);
            });

            var next = 0;
            run.Measure($"Neural — a natural English sentence typed on the {name} layout", english.Count, () =>
            {
                var sentence = english[next++];
                return new Case(machine.AsTargetKeystrokes(sentence), sentence, machine.TargetSource);
            });
        }

        var fixtures = Fixtures(machine).ToList();
        foreach (var (status, rows) in fixtures.GroupBy(f => f.Status).Select(g => (g.Key, g.ToList())))
        {
            var next = 0;
            run.Measure($"Neural — typing fixtures marked {status}", rows.Count, () => rows[next++].Case);
        }

        run.Latency();
    }

    // One slip of the fingers in a word of four letters or more, that does not happen to spell another word:
    // a neighbouring key hit instead (the commonest), two letters swapped, one dropped, or a neighbour
    // hit as well. The line stays as it is if no word allows one.
    private static (string Typed, Slip? Slip) WithTypo(
        Random random, string[] words, KeyboardMap map, Func<string, bool> isWord)
    {
        var longWords = Enumerable.Range(0, words.Length).Where(i => words[i].Length >= 4).ToArray();
        for (var attempt = 0; attempt < 20 && longWords.Length > 0; attempt++)
        {
            var at = longWords[random.Next(longWords.Length)];
            var word = words[at];
            var pos = random.Next(1, word.Length - 1);
            var near = KeyNeighbours.Of(word[pos], map);
            var roll = random.Next(10);
            var typo = roll switch
            {
                < 4 when near.Count > 0 => word[..pos] + near[random.Next(near.Count)] + word[(pos + 1)..],
                < 6 => word[..pos] + word[pos + 1] + word[pos] + word[(pos + 2)..],
                < 8 => word.Remove(pos, 1),
                _ when near.Count > 0 => word.Insert(pos, near[random.Next(near.Count)].ToString()),
                _ => word.Insert(pos, word[pos].ToString())
            };

            if (typo == word || isWord(typo))
                continue;

            var copy = (string[])words.Clone();
            copy[at] = typo;
            return (string.Join(' ', copy), new Slip(at, typo, word));
        }

        return (string.Join(' ', words), null);
    }

    private sealed class Run(Machine machine, NeuralPhraseRewriter brain, RecordingLlm recorder, string? blocks)
    {
        public void Measure(string heading, int count, Func<Case> next)
        {
            if (blocks is not null && !heading.Contains(blocks, StringComparison.OrdinalIgnoreCase))
                return;

            Report.Heading(heading);

            var dictionaryRight = 0;
            var neuralRight = 0;
            var won = 0;
            var lost = 0;
            var slips = 0;
            var offered = 0;
            var modelRight = 0;
            var modelCalls = 0;
            var sources = new Dictionary<string, int>();
            var examples = new List<string>();

            for (var i = 0; i < count; i++)
            {
                var (typed, expected, active, slip) = next();

                var alone = machine.Brain.CorrectPhrase(typed, active, machine.Installed, machine.Candidates).Corrected;

                recorder.Reply = null;
                recorder.Scored = false;
                var clock = Stopwatch.StartNew();
                var got = brain.CorrectPhrase(typed, active, machine.Installed, machine.Candidates).Corrected;
                if (recorder.Reply is not null || recorder.Scored)
                    recorder.Elapsed.Add(clock.Elapsed.TotalMilliseconds);
                var reply = recorder.Reply is { } raw ? NeuralPhraseRewriter.SanitizeModelOutput(raw, typed) : null;

                if (alone == expected) dictionaryRight++;
                if (got == expected) neuralRight++;
                if (got == expected && alone != expected) won++;
                if (alone == expected && got != expected) lost++;
                var missed = false;
                if (slip is { } s)
                {
                    slips++;
                    if (Offered(s, alone, typed)) offered++;
                    else missed = true;
                }
                if (reply is not null)
                {
                    modelCalls++;
                    if (reply == expected.Trim()) modelRight++;
                }

                var source = SourceOf(typed, got, alone, reply, recorder.Scored);
                sources[source] = sources.GetValueOrDefault(source) + 1;

                // Losses to the dictionary first: those are what the neural path has to answer for.
                if (alone == expected && got != expected && examples.Count < 8)
                    examples.Insert(0, $"LOST {typed} → {got}   wanted {expected}");
                else if (missed && examples.Count < 8)
                    examples.Add($"NOT OFFERED {slip!.Value.Typo} → {slip.Value.Meant}   in {typed}");
                else if (got != expected && examples.Count < 8)
                    examples.Add($"{typed} → {got}   wanted {expected}   ({source}{(reply is null || reply == got.Trim() ? "" : $"; model said “{reply}”")})");
            }

            if (count == 0)
                return;

            Report.Row("right (dictionary alone → neural)", (double)dictionaryRight / count, (double)neuralRight / count, Better.Higher);
            Report.Cells("against the dictionary", ["won", "lost"],
                [(double)won / count, (double)lost / count], Better.Either);
            if (slips > 0)
                Report.Value("the meant word among the spellings offered", (double)offered / slips, Better.Higher);
            if (modelCalls > 0)
                Report.Value($"model's own answer right, of {modelCalls} asked", (double)modelRight / modelCalls, Better.Higher);

            var order = new[] { Source.Model, Source.Adviser, Source.Remap, Source.AsTyped, Source.Shortcut };
            Report.Cells("whose answer went out", order,
                order.Select(s => (double)sources.GetValueOrDefault(s) / count).ToList(), Better.Either);

            foreach (var example in examples)
                Report.Line(string.Empty, example);
        }

        // Whether the neural path could have got the slip right at all: the dictionary's own answer, or
        // one of the spellings it offers for the mistyped word.
        private bool Offered(Slip slip, string alone, string typed)
        {
            var aloneWords = Machine.Words(alone);
            if (aloneWords.Length == Machine.Words(typed).Length && aloneWords[slip.At] == slip.Meant)
                return true;

            var script = Scripts.Dominant(slip.Typo) ?? Script.Latin;
            return machine.Brain.SpellSuggestions(slip.Typo, script).Any(s => s.Word == slip.Meant);
        }

        private static string SourceOf(string typed, string got, string alone, string? reply, bool scored)
        {
            if (got == typed)
                return Source.AsTyped;
            if (reply is null && !scored)
                return Source.Shortcut;
            if (got == alone)
                return Source.Adviser;
            return scored || got.Trim() == reply ? Source.Model : Source.Remap;
        }

        public void Latency()
        {
            var times = recorder.Elapsed.Order().ToList();
            Report.Heading("Neural — time per correction that asked the model");
            if (times.Count == 0)
            {
                Report.Line("no model calls", string.Empty);
                return;
            }

            // Printed, not recorded: milliseconds depend on the machine, not on the change under test.
            Report.Line("corrections", times.Count.ToString());
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

    private readonly record struct Slip(int At, string Typo, string Meant);

    // tools/Lapsus.Sweep/sentences/{code}.txt: everyday sentences written for this, one per line.
    private static List<string> Sentences(string code)
    {
        var path = Path.Combine(Baseline.RepositoryRoot() ?? ".", "tools", "Lapsus.Sweep", "sentences", $"{code}.txt");
        return File.Exists(path)
            ? File.ReadLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
            : [];
    }

    private readonly record struct Case(string Typed, string Expected, LayoutSource Active, Slip? Slip = null);

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

        public bool Scored { get; set; }

        public List<double> Elapsed { get; } = [];

        public bool IsLoaded => inner.IsLoaded;

        public void Unload() => inner.Unload();

        public async Task<string> CompleteAsync(
            string systemPrompt, string userText, int answerChars, CancellationToken cancellationToken = default)
        {
            var reply = await inner.CompleteAsync(systemPrompt, userText, answerChars, cancellationToken).ConfigureAwait(false);
            Reply = reply;
            return reply;
        }

        public Task<IReadOnlyList<double>?> ScoreAsync(
            string context, IReadOnlyList<string> texts, bool ends, CancellationToken cancellationToken = default)
        {
            Scored = true;
            return inner.ScoreAsync(context, texts, ends, cancellationToken);
        }

        public void Dispose()
        {
        }
    }
}
