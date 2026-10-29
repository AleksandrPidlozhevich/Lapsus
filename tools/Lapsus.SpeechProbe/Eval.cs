// The dictation pipeline as the app will run it, layer by layer:
//
//   eval <model.bin> <vad.bin> <samplesDir> <dictDir> [--lang ru] [--langs ru,en] [--device gpu|cpu]
//        [--glossary terms.txt] [--limit N]
//   restore <dictDir> <lang> <text…>      the term restorer alone, on text
//
// dictDir holds {code}.txt frequency lists (and optional {code}.aff/.dic), as the app keeps them. Each block
// reads "raw → filtered → restored": what Whisper wrote with the prompt, after the hallucination filter, and
// after English terms were put back into Latin.

using System.Diagnostics;
using System.Text.RegularExpressions;
using Lapsus.Core.Layout;
using Lapsus.Core.Speech;
using Lapsus.Core.Spelling;
using Lapsus.Speech;

static class Eval
{
    public static async Task<int> RunAsync(string modelPath, string vadPath, string samplesDir, string dictDir, string[] args)
    {
        string lang = "ru", device = "gpu";
        string[]? langs = null;
        string? glossaryPath = null;
        var limit = int.MaxValue;
        for (var i = 0; i < args.Length; i += 2)
        {
            var value = i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--lang": lang = value; break;
                case "--langs": langs = value.Split(','); break;
                case "--device": device = value; break;
                case "--glossary": glossaryPath = value; break;
                case "--limit": limit = int.Parse(value); break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        }

        var spell = LoadDictionaries(dictDir, out var loaded);
        if (!spell.Has(Script.Latin) || !loaded.Contains(lang))
        {
            Console.Error.WriteLine($"{dictDir} needs en.txt and {lang}.txt — without them the restorer has nothing to judge by");
            return 1;
        }

        var glossary = glossaryPath is null ? [] : File.ReadAllLines(glossaryPath).Where(l => l.Trim().Length > 0).ToArray();
        var restorer = new TermRestorer(spell, glossary);
        var candidates = DictationLanguagePicker.Candidates(lang, langs ?? [lang, "en"]);

        var samples = Probe.LoadSamples(samplesDir).Take(limit).ToList();
        if (samples.Count == 0)
        {
            Console.Error.WriteLine($"no .wav files in {samplesDir}");
            return 1;
        }
        samples.Add(Sample.Made("~silence", new float[16000 * 5]));
        samples.Add(Sample.Made("~noise", Probe.Noise(16000 * 5, 0.01f)));

        using var recognizer = new WhisperRecognizer();
        var sw = Stopwatch.StartNew();
        recognizer.Load(modelPath, vadPath, device == "gpu");
        var loadMs = sw.ElapsedMilliseconds;
        if (Environment.GetEnvironmentVariable("LAPSUS_WHISPER_LOG") is not null)
            foreach (var line in recognizer.LoadLog)
                Console.Write(line);
        sw.Restart();
        await recognizer.WarmUpAsync();
        Console.WriteLine($"model {Path.GetFileName(modelPath)} on {recognizer.DeviceLabel}: load {loadMs} ms, warm-up {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"dictionaries {string.Join(",", loaded)}; detection among {string.Join(",", candidates)}");

        var stages = new[] { "raw", "filtered", "restored" };
        var totals = stages.ToDictionary(s => s, _ => new Score());
        var quiet = new List<string>();
        double audio = 0, decode = 0;

        foreach (var s in samples)
        {
            // Recordings are separate phrases, so no earlier text goes into the prompt.
            var prompt = SpeechPromptBuilder.Build(lang, glossary.Select(g => g.Split('|')[0]));
            sw.Restart();
            var result = await recognizer.TranscribeAsync(s.Pcm, new SpeechRequest(lang, prompt, candidates));
            var decodeMs = sw.ElapsedMilliseconds;
            var filtered = HallucinationFilter.Clean(result.Text);
            sw.Restart();
            var restored = restorer.Restore(filtered, result.Language);
            var restoreMs = sw.ElapsedMilliseconds;

            if (s.Synthetic)
            {
                quiet.Add($"{s.Name}: raw \"{result.Text}\" → \"{restored}\"");
                continue;
            }

            audio += s.Seconds;
            decode += decodeMs / 1000.0;
            Console.WriteLine();
            Console.WriteLine($"{s.Name}  {decodeMs} ms + {restoreMs} ms  [{result.Language} {result.LanguageProbability:F2}]");
            Console.WriteLine($"  raw      {result.Text}");
            if (filtered != result.Text) Console.WriteLine($"  filtered {filtered}");
            if (restored != filtered) Console.WriteLine($"  restored {restored}");
            if (s.Reference is not null) Console.WriteLine($"  want     {s.Reference}");
            if (s.Reference is null)
                continue;

            totals["raw"].Add(s.Reference, result.Text);
            totals["filtered"].Add(s.Reference, filtered);
            totals["restored"].Add(s.Reference, restored);
        }

        Console.WriteLine();
        foreach (var line in quiet) Console.WriteLine(line);
        Console.WriteLine();
        Console.WriteLine($"{"stage",-9} {"WER",6} {"CER",6} {"terms",7} {"false Latin",12}");
        foreach (var stage in stages)
            Console.WriteLine($"{stage,-9} {totals[stage]}");
        Console.WriteLine($"decode: {1000 * decode / samples.Count(x => !x.Synthetic):F0} ms a phrase, RTF {decode / audio:F2}");
        Console.WriteLine("terms: Latin words of the reference written exactly. false Latin: Latin words where the reference has none.");
        return 0;
    }

    public static int Restore(string dictDir, string lang, string[] text)
    {
        var spell = LoadDictionaries(dictDir, out var loaded);
        Console.WriteLine($"dictionaries {string.Join(",", loaded)}");
        var restorer = new TermRestorer(spell);
        foreach (var line in text)
        {
            var sw = Stopwatch.StartNew();
            var restored = restorer.Restore(line, lang);
            Console.WriteLine($"{line}\n  → {restored}   ({sw.ElapsedMilliseconds} ms)");
        }
        return 0;
    }

    private static SpellChecker LoadDictionaries(string dir, out List<string> loaded)
    {
        var sources = new List<DictionarySource>();
        loaded = [];
        foreach (var txt in Directory.GetFiles(dir, "*.txt"))
        {
            var code = Path.GetFileNameWithoutExtension(txt);
            var first = File.ReadLines(txt).Select(l => l.Split(' ', '\t')[0]).FirstOrDefault(w => w.Length > 2);
            if (first is null || Scripts.Dominant(first) is not { } script)
                continue;
            var dic = Path.ChangeExtension(txt, ".dic");
            sources.Add(new DictionarySource(code, txt, script, File.Exists(dic) ? dic : null));
            loaded.Add(code);
        }
        return new SpellChecker(sources);
    }

    private sealed class Score
    {
        private double _words, _wordErrors, _chars, _charErrors, _terms, _termsHit, _falseLatin;

        public void Add(string reference, string hypothesis)
        {
            var (rw, hw) = (Probe.Words(reference), Probe.Words(hypothesis));
            _words += rw.Length;
            _wordErrors += Probe.Distance(rw, hw);
            var (rc, hc) = (string.Join(' ', rw).ToCharArray(), string.Join(' ', hw).ToCharArray());
            _chars += rc.Length;
            _charErrors += Probe.Distance(rc, hc);

            var terms = Probe.LatinTerms(reference).ToList();
            foreach (var term in terms)
            {
                _terms++;
                if (Regex.IsMatch(hypothesis, $@"(?<![\p{{L}}]){Regex.Escape(term)}(?![\p{{L}}])", RegexOptions.IgnoreCase))
                    _termsHit++;
            }

            var wanted = new HashSet<string>(terms.SelectMany(Probe.Words), StringComparer.OrdinalIgnoreCase);
            if (hw.Any(w => !Regex.IsMatch(w, @"^[a-z]+$")))
                _falseLatin += hw.Count(w => Regex.IsMatch(w, @"^[a-z]+$") && !wanted.Contains(w));
        }

        public override string ToString() =>
            $"{100 * _wordErrors / Math.Max(1, _words),5:F1}% {100 * _charErrors / Math.Max(1, _chars),5:F1}% " +
            $"{_termsHit,3}/{_terms,-3} {_falseLatin,12}";
    }
}
