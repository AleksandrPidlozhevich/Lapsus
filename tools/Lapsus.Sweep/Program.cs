using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Models;
using Lapsus.Core.Spelling;
using Lapsus.Sweep;

var targets = TargetLanguage.Priority;
var langIndex = Array.IndexOf(args, "--lang");
if (langIndex >= 0)
{
    var langCode = langIndex + 1 < args.Length ? args[langIndex + 1] : string.Empty;
    targets = TargetLanguage.ByCode(langCode);
    if (targets.Length == 0)
    {
        Console.Error.WriteLine($"Unknown --lang {langCode}. Known: {string.Join(", ", TargetLanguage.All.Select(l => l.Key))}.");
        return 1;
    }
}

var baselineDirIndex = Array.IndexOf(args, "--baseline-dir");
var baselineDir = baselineDirIndex >= 0 && baselineDirIndex + 1 < args.Length
    ? args[baselineDirIndex + 1]
    : Baseline.DefaultDirectory();

var directory = args.Where((a, i) => !a.StartsWith('-') && (i == 0 || args[i - 1] is not
                        ("--lang" or "--baseline-dir" or "--auto-min" or "--punct-head-start" or "--sample-dir" or "--index-size" or "--lexicon-min"
                            or "--neural" or "--device" or "--blocks")))
                    .FirstOrDefault()
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus", "dictionaries");

var sampleDirIndex = Array.IndexOf(args, "--sample-dir");
var sampleDirectory = sampleDirIndex >= 0 && sampleDirIndex + 1 < args.Length ? args[sampleDirIndex + 1] : directory;

var indexSizeIndex = Array.IndexOf(args, "--index-size");
var indexSize = indexSizeIndex >= 0 && indexSizeIndex + 1 < args.Length && int.TryParse(args[indexSizeIndex + 1], out var size)
    ? size
    : SpellChecker.DefaultSuggestionIndexSize;

// --neural <model folder or installed model id>: measure the neural brain instead. A model call costs
// hundreds of milliseconds, so it runs on far fewer lines, and the rules below are not run.
var neuralIndex = Array.IndexOf(args, "--neural");
string? modelDirectory = null;
if (neuralIndex >= 0)
{
    var model = neuralIndex + 1 < args.Length ? args[neuralIndex + 1] : string.Empty;
    modelDirectory = Directory.Exists(model)
        ? model
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus", "models", model);
    if (model.Length == 0 || model.StartsWith('-') || !Directory.Exists(modelDirectory))
    {
        Console.Error.WriteLine($"--neural needs a model folder or the id of a model installed in the app; no model at \"{modelDirectory}\".");
        return 1;
    }
}

// --blocks <text>: only the neural blocks whose heading contains it, e.g. "typo".
var blocksIndex = Array.IndexOf(args, "--blocks");
var blocks = blocksIndex >= 0 && blocksIndex + 1 < args.Length ? args[blocksIndex + 1] : null;

var deviceIndex = Array.IndexOf(args, "--device");
var device = ComputeDevicePreference.Auto;
if (deviceIndex >= 0 && (deviceIndex + 1 >= args.Length || !Enum.TryParse(args[deviceIndex + 1], true, out device)))
{
    Console.Error.WriteLine($"--device takes one of: {string.Join(", ", Enum.GetNames<ComputeDevicePreference>())}.");
    return 1;
}

var lines = modelDirectory is not null
    ? args.Contains("--quick") ? 25 : 150
    : args.Contains("--quick") ? 2000 : 20000;
var ngrams = !args.Contains("--vowels");
var clitics = !args.Contains("--no-clitics");
var scoring = (ngrams ? "trigrams" : "vowel ratio") + (clitics ? string.Empty : ", no clitics");
var autoMinIndex = Array.IndexOf(args, "--auto-min");
var autoMin = autoMinIndex >= 0 && autoMinIndex + 1 < args.Length && int.TryParse(args[autoMinIndex + 1], out var parsed)
    ? parsed
    : AutoCorrectPolicy.MinLength;
if (autoMin != AutoCorrectPolicy.MinLength)
    scoring += $", auto mode from {autoMin} letters";

var headStartIndex = Array.IndexOf(args, "--punct-head-start");
var headStart = headStartIndex >= 0 && headStartIndex + 1 < args.Length &&
                double.TryParse(args[headStartIndex + 1], System.Globalization.CultureInfo.InvariantCulture, out var given)
    ? given
    : LayoutCorrector.DefaultPunctuationHeadStart;
if (Math.Abs(headStart - LayoutCorrector.DefaultPunctuationHeadStart) > 1e-9)
    scoring += $", punctuation head start {headStart}";

var lexicon = !args.Contains("--no-lexicon");
var lexiconMinIndex = Array.IndexOf(args, "--lexicon-min");
int? lexiconMin = lexiconMinIndex >= 0 && lexiconMinIndex + 1 < args.Length && int.TryParse(args[lexiconMinIndex + 1], out var letters)
    ? letters
    : null;

string? WordForms(string code)
{
    var path = Path.Combine(directory, $"{code}.dic");
    return lexicon && File.Exists(path) ? path : null;
}

var vocabulary = $"typo index {indexSize} words, " + (!lexicon
    ? "Hunspell off"
    : $"Hunspell from {(lexiconMin is { } min ? $"{min} letters" : "the default length")} for: " +
      string.Join(" ", new[] { "en" }.Concat(targets.Select(t => t.Code)).Distinct().Where(c => WordForms(c) is not null)
          .DefaultIfEmpty("none")));

var writeBaseline = args.Contains("--write-baseline");
var compare = args.Contains("--compare");

foreach (var dir in new[] { directory, sampleDirectory }.Distinct())
{
    var missing = targets.Where(t => !File.Exists(Path.Combine(dir, $"{t.Code}.txt"))).Select(t => $"{t.Code}.txt").ToList();
    if (!File.Exists(Path.Combine(dir, "en.txt")))
        missing.Insert(0, "en.txt");

    if (missing.Count > 0)
    {
        Console.Error.WriteLine($"No {string.Join(", ", missing)} under {dir}.");
        Console.Error.WriteLine("Download them in the app's settings, or pass the directory as the first argument.");
        return 1;
    }
}

var en = FrequencyList.Load(Path.Combine(sampleDirectory, "en.txt"));

var (kept, heldOut) = en.HoldOutEveryNth(100);
var scratch = Path.Combine(Path.GetTempPath(), $"lapsus-sweep-{Guid.NewGuid():N}");
Directory.CreateDirectory(scratch);
var trimmedEn = Path.Combine(scratch, "en-trimmed.txt");
if (sampleDirectory == directory)
    File.WriteAllLines(trimmedEn, kept.Select(w => $"{w.Word} {w.Count}"));
else
    WithoutHeldOut(Path.Combine(directory, "en.txt"), heldOut, trimmedEn);

var known = FrequencyList.FromWords(kept);

var neural = modelDirectory is null ? null : NeuralRun.Load(modelDirectory, device);
if (neural is { } loaded)
{
    scoring += $", neural {loaded.Name} on {loaded.Device}";
    Console.WriteLine($"Model: {Path.GetFullPath(modelDirectory!)} on {loaded.Device}");
}

var regressed = new List<string>();
try
{
    foreach (var target in targets)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 100));
        var samples = sampleDirectory == directory ? string.Empty : $"   words drawn from: {sampleDirectory}";
        Console.WriteLine($"Lists: {directory}{samples}   target: {target.Label} on the English layout   lines per measurement: {lines}   unknown words scored by: {scoring}");
        Console.WriteLine($"Vocabulary: {vocabulary}");

        var targetPath = Path.Combine(directory, $"{target.Code}.txt");
        var foreign = FrequencyList.Load(Path.Combine(sampleDirectory, $"{target.Code}.txt"), target, out var stats);
        Console.WriteLine($"en {en.Count} words (max {en.MaxCount}), held out {heldOut.Count} as out-of-vocabulary   {target.Code} {stats} (max {foreign.MaxCount})");

        var sources = new[]
        {
            new DictionarySource("en", trimmedEn, Script.Latin, WordForms("en")),
            new DictionarySource(target.Code, targetPath, target.Script, WordForms(target.Code))
        };

        var machine = new Machine(new SpellChecker(sources, ngrams, clitics, indexSize, lexicon, lexiconMin), target, headStart);

        Report.Reset();
        if (neural is { } model)
        {
            NeuralRun.Measure(machine, model.Llm, known, foreign, lines, blocks);
        }
        else
        {
            var (keptTarget, heldOutTarget) = foreign.HoldOutEveryNth(100);
            var trimmedTarget = Path.Combine(scratch, $"{target.Key}-trimmed.txt");
            if (sampleDirectory == directory)
                File.WriteAllLines(trimmedTarget, keptTarget.Select(w => $"{w.Word} {w.Count}"));
            else
                WithoutHeldOut(targetPath, heldOutTarget, trimmedTarget);
            var shortOfWords = new Machine(
                new SpellChecker(
                    [
                        new DictionarySource("en", trimmedEn, Script.Latin, WordForms("en")),
                        new DictionarySource(target.Code, trimmedTarget, target.Script, WordForms(target.Code))
                    ],
                    ngrams, clitics, indexSize, lexicon, lexiconMin),
                target, headStart);

            RuleB.Recall(machine, foreign, lines);
            RuleB.FalsePositives(machine, known, foreign, lines);
            RuleA.Recall(machine, known, heldOut, lines);
            RuleA.RecallInMixedLine(machine, foreign, heldOut, lines);
            RuleA.FalsePositives(machine, foreign, lines);
            RuleC.Targets(machine, known, foreign, lines);
            RuleD.TyposStayEnglish(machine, known, lines);
            RuleE.AutoMode(machine, known, foreign, lines / 4, autoMin);
            RuleF.HeldOutWords(shortOfWords, FrequencyList.FromWords(keptTarget), heldOutTarget, lines);
        }

        // Neural baselines are per model: one model's numbers say nothing about another's.
        var key = neural is { } measured ? $"neural-{measured.Name}-{target.Key}" : target.Key;
        var current = new Baseline(key, lines, scoring, foreign.Count, new Dictionary<string, Metric>(Report.Metrics));

        if (compare)
        {
            var previous = Baseline.Read(baselineDir, key);
            if (previous is null)
                Report.Line("no baseline", Baseline.PathFor(baselineDir, key));
            else if (!previous.Compare(current))
                regressed.Add(key);
        }

        if (writeBaseline)
        {
            current.Write(baselineDir);
            Report.Line("baseline written", Baseline.PathFor(baselineDir, key));
        }
    }
}
finally
{
    Directory.Delete(scratch, true);
    neural?.Llm.Dispose();
}

if (regressed.Count == 0)
    return 0;

Console.Error.WriteLine();
Console.Error.WriteLine($"Regressed against baseline: {string.Join(", ", regressed)}.");
return 2;

static void WithoutHeldOut(string listPath, List<FrequencyWord> heldOut, string outputPath)
{
    var drop = heldOut.Select(w => w.Word).ToHashSet(StringComparer.Ordinal);
    File.WriteAllLines(outputPath, File.ReadLines(listPath).Where(line =>
    {
        var space = line.IndexOfAny([' ', '\t']);
        return space <= 0 || !drop.Contains(line[..space].ToLowerInvariant());
    }));
}
