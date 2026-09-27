using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
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
                        ("--lang" or "--baseline-dir" or "--auto-min" or "--punct-head-start" or "--sample-dir" or "--index-size" or "--lexicon-min")))
                    .FirstOrDefault()
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus", "dictionaries");

var sampleDirIndex = Array.IndexOf(args, "--sample-dir");
var sampleDirectory = sampleDirIndex >= 0 && sampleDirIndex + 1 < args.Length ? args[sampleDirIndex + 1] : directory;

var indexSizeIndex = Array.IndexOf(args, "--index-size");
var indexSize = indexSizeIndex >= 0 && indexSizeIndex + 1 < args.Length && int.TryParse(args[indexSizeIndex + 1], out var size)
    ? size
    : SpellChecker.DefaultSuggestionIndexSize;

var lines = args.Contains("--quick") ? 2000 : 20000;
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

        Report.Reset();
        RuleB.Recall(machine, foreign, lines);
        RuleB.FalsePositives(machine, known, foreign, lines);
        RuleA.Recall(machine, known, heldOut, lines);
        RuleA.RecallInMixedLine(machine, foreign, heldOut, lines);
        RuleA.FalsePositives(machine, foreign, lines);
        RuleC.Targets(machine, known, foreign, lines);
        RuleD.TyposStayEnglish(machine, known, lines);
        RuleE.AutoMode(machine, known, foreign, lines / 4, autoMin);
        RuleF.HeldOutWords(shortOfWords, FrequencyList.FromWords(keptTarget), heldOutTarget, lines);

        var current = new Baseline(target.Key, lines, scoring, foreign.Count, new Dictionary<string, Metric>(Report.Metrics));

        if (compare)
        {
            var previous = Baseline.Read(baselineDir, target.Key);
            if (previous is null)
                Report.Line("no baseline", Baseline.PathFor(baselineDir, target.Key));
            else if (!previous.Compare(current))
                regressed.Add(target.Key);
        }

        if (writeBaseline)
        {
            current.Write(baselineDir);
            Report.Line("baseline written", Baseline.PathFor(baselineDir, target.Key));
        }
    }
}
finally
{
    Directory.Delete(scratch, true);
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
