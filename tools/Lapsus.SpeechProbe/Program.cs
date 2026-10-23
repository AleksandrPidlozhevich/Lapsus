// Stage 1 probe for voice dictation. See the csproj for what it is for.
//
//   record <samplesDir> <phrases.txt>        read each phrase into the microphone (Enter = start / stop)
//   run <model.bin> <samplesDir> [options]   transcribe every NN.wav, compare with NN.txt when present
//
// run options:
//   --lang ru            language for the fixed-language variants (default ru)
//   --langs ru,uk,en     languages auto detection may choose from (default: --lang plus en)
//   --device cpu|gpu|both (default both)
//   --threads N          CPU threads (default: all logical cores)
//   --prompt "..."       mixed-language prompt (default: a built-in one per language)
//   --vad <silero.bin>   trim audio to the speech Silero VAD finds before decoding
//   --only <name>        run only variants whose name contains this
//   --limit N            use only the first N recordings
//   --runtime vulkan|cpu native build to load (default vulkan; its CPU path also serves --device cpu)

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Wave;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: record <samplesDir> <phrases.txt> | run <model.bin> <samplesDir> [options]");
    return 1;
}

return args[0] switch
{
    "record" when args.Length >= 3 => Recorder.Run(args[1], args[2]),
    "run" when args.Length >= 3 => await Probe.RunAsync(args[1], args[2], Options.Parse(args[3..])),
    "eval" when args.Length >= 5 => await Eval.RunAsync(args[1], args[2], args[3], args[4], args[5..]),
    "restore" when args.Length >= 4 => Eval.Restore(args[1], args[2], args[3..]),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: record <samplesDir> <phrases.txt> | run <model.bin> <samplesDir> [options]");
    return 1;
}

sealed record Options(
    string Lang, string[] Langs, string Device, int Threads, string? Prompt, string? Vad, string? Only,
    int Limit = int.MaxValue, string Runtime = "vulkan")
{
    public static Options Parse(string[] a)
    {
        string lang = "ru", device = "both", runtime = "vulkan";
        string[]? langs = null;
        int threads = Environment.ProcessorCount, limit = int.MaxValue;
        string? prompt = null, vad = null, only = null;
        for (var i = 0; i < a.Length; i++)
        {
            var next = i + 1 < a.Length ? a[i + 1] : throw new ArgumentException($"{a[i]} needs a value");
            switch (a[i])
            {
                case "--lang": lang = next; i++; break;
                case "--langs": langs = next.Split(',', StringSplitOptions.RemoveEmptyEntries); i++; break;
                case "--device": device = next; i++; break;
                case "--threads": threads = int.Parse(next); i++; break;
                case "--prompt": prompt = next; i++; break;
                case "--vad": vad = next; i++; break;
                case "--only": only = next; i++; break;
                case "--limit": limit = int.Parse(next); i++; break;
                case "--runtime": runtime = next; i++; break;
                default: throw new ArgumentException($"unknown option {a[i]}");
            }
        }
        langs ??= lang == "en" ? ["en"] : [lang, "en"];
        return new Options(lang, langs, device, threads, prompt, vad, only, limit, runtime);
    }
}

// One way of decoding: sampling strategy, fixed or detected language, prompt or not.
sealed record Variant(string Name, bool Beam, bool Prompt, bool Detect, bool ShortCtx = false);

static class Probe
{
    // The strongest lever for keeping English terms in Latin: show the decoder a sentence that already does it.
    static readonly Dictionary<string, string> MixedPrompts = new()
    {
        ["ru"] = "Закинул pull request в GitHub, после code review задеплою на staging и обновлю README.",
        ["uk"] = "Закинув pull request у GitHub, після code review задеплою на staging і оновлю README.",
        ["be"] = "Закінуў pull request у GitHub, пасля code review задэплою на staging.",
        ["bg"] = "Пуснах pull request в GitHub, след code review ще го deploy-на на staging.",
        ["en"] = "Opened a pull request on GitHub; after code review I'll deploy to staging.",
    };

    static readonly Variant[] Variants =
    [
        new("greedy", Beam: false, Prompt: false, Detect: false),
        new("beam5", Beam: true, Prompt: false, Detect: false),
        new("beam5+prompt", Beam: true, Prompt: true, Detect: false),
        new("detect+beam5+prompt", Beam: true, Prompt: true, Detect: true),
        new("greedy+prompt", Beam: false, Prompt: true, Detect: false),
        new("greedy+prompt+ctx", Beam: false, Prompt: true, Detect: false, ShortCtx: true),
        new("beam5+prompt+ctx", Beam: true, Prompt: true, Detect: false, ShortCtx: true),
    ];

    public static async Task<int> RunAsync(string modelPath, string samplesDir, Options o)
    {
        if (!File.Exists(modelPath)) return Fail($"model not found: {modelPath}");
        if (!Directory.Exists(samplesDir)) return Fail($"samples folder not found: {samplesDir}");

        var samples = LoadSamples(samplesDir).Take(o.Limit).ToList();
        if (samples.Count == 0) return Fail($"no .wav files in {samplesDir}");
        samples.Add(Sample.Made("~silence", new float[16000 * 5]));
        samples.Add(Sample.Made("~noise", Noise(16000 * 5, 0.01f)));

        // Vulkan first; the same native build also runs on CPU when the factory is told not to use the GPU,
        // so one process can measure both devices.
        RuntimeOptions.RuntimeLibraryOrder = o.Runtime == "cpu"
            ? [RuntimeLibrary.Cpu]
            : [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];

        var prompt = o.Prompt ?? MixedPrompts.GetValueOrDefault(o.Lang, MixedPrompts["en"]);
        var variants = Variants.Where(v => o.Only is null || v.Name.Contains(o.Only)).ToArray();
        var devices = o.Device == "both" ? new[] { "gpu", "cpu" } : [o.Device];

        Console.WriteLine($"model    {Path.GetFileName(modelPath)} ({new FileInfo(modelPath).Length / 1048576} MB)");
        Console.WriteLine($"samples  {samples.Count - 2} files + silence + noise, {samples.Sum(s => s.Seconds):F1} s of audio");
        Console.WriteLine($"lang     {o.Lang}; detection among {string.Join(",", o.Langs)}; threads {o.Threads}");
        Console.WriteLine($"prompt   {prompt}");

        WhisperVadProcessor? vad = null;
        if (o.Vad is not null)
        {
            var vadFactory = WhisperVadFactory.FromPath(o.Vad);
            vad = vadFactory.CreateBuilder().WithThreshold(0.5f)
                .WithMinSilenceDuration(TimeSpan.FromMilliseconds(300))
                .WithSpeechPadding(TimeSpan.FromMilliseconds(200)).Build();
            Console.WriteLine($"vad      {Path.GetFileName(o.Vad)}");
        }

        var summary = new List<string>();
        foreach (var device in devices)
        {
            Console.WriteLine();
            Console.WriteLine($"================ {device.ToUpperInvariant()} ================");

            // Two loads in a row: the first may read the file from disk, the second comes from the OS file cache.
            var (factory, coldMs) = Load(modelPath, device == "gpu");
            Console.WriteLine($"runtime  {RuntimeOptions.LoadedLibrary}  {WhisperFactory.GetRuntimeInfo()?.Trim()}");
            factory.Dispose();
            (factory, var warmMs) = Load(modelPath, device == "gpu");
            Console.WriteLine($"load     first {coldMs} ms, again {warmMs} ms");

            using (factory)
            {
                foreach (var v in variants)
                {
                    var r = await RunVariant(factory, v, samples, o, prompt, vad);
                    summary.Add($"{device,-4} {v.Name,-22} {r}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("================ SUMMARY ================");
        Console.WriteLine($"{"dev",-4} {"variant",-22} {"WER",6} {"CER",6} {"terms",9} {"RTF",6} {"avg ms",7}  silence/noise");
        foreach (var line in summary) Console.WriteLine(line);
        Console.WriteLine("WER/CER: lower is better. terms: Latin words of the reference written exactly. "
            + "RTF: decode time / audio time.");
        vad?.Dispose();
        return 0;
    }

    static (WhisperFactory, long) Load(string modelPath, bool gpu)
    {
        var sw = Stopwatch.StartNew();
        var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = gpu });
        // The model weights load on the first processor, so build one to make the timing honest.
        using (factory.CreateBuilder().WithLanguage("en").Build()) { }
        return (factory, sw.ElapsedMilliseconds);
    }

    static async Task<string> RunVariant(
        WhisperFactory factory, Variant v, List<Sample> samples, Options o, string prompt, WhisperVadProcessor? vad)
    {
        WhisperProcessor Build(int audioCtx)
        {
            var builder = factory.CreateBuilder()
                .WithLanguage(o.Lang)
                .WithThreads(o.Threads)
                .WithNoContext()
                .WithSingleSegment()
                .WithProbabilities();
            builder = v.Beam
                ? builder.WithBeamSearchSamplingStrategy(b => b.WithBeamSize(5))
                : builder.WithGreedySamplingStrategy(_ => { });
            if (v.Prompt) builder = builder.WithPrompt(prompt);
            if (audioCtx > 0) builder = builder.WithAudioContextSize(audioCtx);
            return builder.Build();
        }

        // Whisper always encodes a 30 s window (1500 frames, 50 per second). For a short phrase the encoder
        // can be told to look at only the frames that hold audio, plus a second of slack.
        static int ContextFor(float[] pcm) =>
            Math.Clamp((int)Math.Ceiling((pcm.Length / 16000.0 + 1.0) * 50 / 64.0) * 64, 384, 1500);

        var shared = v.ShortCtx ? null : Build(0);

        Console.WriteLine();
        Console.WriteLine($"--- {v.Name}");

        double words = 0, wordErrors = 0, chars = 0, charErrors = 0, terms = 0, termsHit = 0;
        double audio = 0, decode = 0;
        var quiet = new List<string>();
        foreach (var s in samples)
        {
            var pcm = vad is null ? s.Pcm : Trim(vad, s.Pcm);
            var sw = Stopwatch.StartNew();
            var processor = shared ?? Build(ContextFor(pcm));
            var lang = o.Lang;
            var detectNote = "";
            if (v.Detect && pcm.Length > 0)
            {
                var (detected, p) = processor.DetectLanguageWithProbability(pcm, o.Langs);
                lang = detected ?? o.Lang;
                processor.ChangeLanguage(lang);
                detectNote = $" [{lang} {p:F2}]";
            }

            var text = new StringBuilder();
            float minP = 1;
            if (pcm.Length > 0)
            {
                await foreach (var seg in processor.ProcessAsync(pcm))
                {
                    text.Append(seg.Text);
                    minP = Math.Min(minP, seg.MinProbability);
                }
            }
            sw.Stop();
            if (v.Detect) processor.ChangeLanguage(o.Lang);
            if (shared is null) await processor.DisposeAsync();

            var hyp = text.ToString().Trim();
            if (s.Synthetic)
            {
                quiet.Add(hyp.Length == 0 ? "ok" : $"\"{hyp}\"");
                Console.WriteLine($"  {s.Name,-10} {sw.ElapsedMilliseconds,6} ms{detectNote}  → {(hyp.Length == 0 ? "(empty)" : hyp)}");
                continue;
            }

            audio += s.Seconds;
            decode += sw.Elapsed.TotalSeconds;
            Console.WriteLine($"  {s.Name,-10} {sw.ElapsedMilliseconds,6} ms{detectNote}  minP {minP:F2}");
            Console.WriteLine($"    got  {hyp}");
            if (s.Reference is null) continue;

            Console.WriteLine($"    want {s.Reference}");
            var (rw, hw) = (Words(s.Reference), Words(hyp));
            words += rw.Length;
            wordErrors += Distance(rw, hw);
            var (rc, hc) = (string.Join(' ', rw).ToCharArray(), string.Join(' ', hw).ToCharArray());
            chars += rc.Length;
            charErrors += Distance(rc, hc);
            foreach (var term in LatinTerms(s.Reference))
            {
                terms++;
                if (Regex.IsMatch(hyp, $@"(?<![\p{{L}}]){Regex.Escape(term)}(?![\p{{L}}])", RegexOptions.IgnoreCase))
                    termsHit++;
            }
        }

        if (shared is not null) await shared.DisposeAsync();
        var n = samples.Count(s => !s.Synthetic);
        return string.Join(' ',
            words > 0 ? $"{100 * wordErrors / words,5:F1}%" : "     -",
            chars > 0 ? $"{100 * charErrors / chars,5:F1}%" : "     -",
            terms > 0 ? $"{termsHit,3}/{terms,-3}{"",2}" : "        -",
            audio > 0 ? $"{decode / audio,6:F2}" : "     -",
            n > 0 ? $"{1000 * decode / n,7:F0}" : "      -",
            " " + string.Join(" / ", quiet));
    }

    // Keeps only the stretches Silero VAD calls speech, glued together.
    static float[] Trim(WhisperVadProcessor vad, float[] pcm)
    {
        var kept = new List<float>();
        foreach (var seg in vad.DetectSpeech(pcm))
        {
            var from = Math.Clamp((int)(seg.Start.TotalSeconds * 16000), 0, pcm.Length);
            var to = Math.Clamp((int)(seg.End.TotalSeconds * 16000), from, pcm.Length);
            kept.AddRange(pcm.AsSpan(from, to - from).ToArray());
        }
        return kept.Count < 16000 * 0.3 ? [] : [.. kept];
    }

    internal static List<Sample> LoadSamples(string dir)
    {
        var list = new List<Sample>();
        foreach (var wav in Directory.GetFiles(dir, "*.wav").Order(StringComparer.Ordinal))
        {
            using var stream = File.OpenRead(wav);
            var parser = new WaveParser(stream);
            parser.Initialize();
            if (parser.SampleRate != 16000)
                throw new InvalidDataException($"{Path.GetFileName(wav)}: {parser.SampleRate} Hz, need 16000");
            var pcm = parser.GetAvgSamples();
            var txt = Path.ChangeExtension(wav, ".txt");
            list.Add(new Sample(Path.GetFileNameWithoutExtension(wav), pcm,
                File.Exists(txt) ? File.ReadAllText(txt).Trim() : null, false));
        }
        return list;
    }

    internal static float[] Noise(int n, float amplitude)
    {
        var rng = new Random(7);
        var a = new float[n];
        for (var i = 0; i < n; i++) a[i] = (float)(rng.NextDouble() * 2 - 1) * amplitude;
        return a;
    }

    internal static string[] Words(string s) =>
        Regex.Matches(s.ToLowerInvariant().Replace('ё', 'е'), @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*")
            .Select(m => m.Value).ToArray();

    // Latin words and phrases inside a non-Latin reference: the terms we want written as-is.
    internal static IEnumerable<string> LatinTerms(string reference)
    {
        var words = Words(reference);
        if (words.All(w => Regex.IsMatch(w, @"^[a-z0-9'’\-]+$"))) return [];
        return Regex.Matches(reference, @"[A-Za-z][A-Za-z0-9.+#\-]*(?:\s+[A-Za-z][A-Za-z0-9.+#\-]*)*")
            .Select(m => m.Value.Trim().TrimEnd('.', '-'));
    }

    internal static int Distance<T>(T[] a, T[] b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + (EqualityComparer<T>.Default.Equals(a[i - 1], b[j - 1]) ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}

sealed record Sample(string Name, float[] Pcm, string? Reference, bool Synthetic)
{
    public double Seconds => Pcm.Length / 16000.0;
    public static Sample Made(string name, float[] pcm) => new(name, pcm, null, true);
}

static class Recorder
{
    public static int Run(string samplesDir, string phrasesFile)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("recording is Windows-only in the probe");
            return 1;
        }
        Directory.CreateDirectory(samplesDir);
        var phrases = File.ReadAllLines(phrasesFile)
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();
        var existing = Directory.GetFiles(samplesDir, "*.wav").Length;

        Console.WriteLine($"{phrases.Length} phrases. Enter = start, Enter = stop, s = skip, q = quit.");
        for (var i = 0; i < phrases.Length; i++)
        {
            var name = $"{existing + i + 1:D3}";
            Console.WriteLine();
            Console.WriteLine($"[{i + 1}/{phrases.Length}] {phrases[i]}");
            Console.Write("  Enter to start… ");
            var key = Console.ReadLine();
            if (key == "q") break;
            if (key == "s") continue;

            var wav = Path.Combine(samplesDir, name + ".wav");
            var seconds = WindowsRecorder.RecordUntilEnter(wav);
            File.WriteAllText(Path.Combine(samplesDir, name + ".txt"), phrases[i]);
            Console.WriteLine($"  saved {name}.wav ({seconds:F1} s)");
        }
        return 0;
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static class WindowsRecorder
{
    public static double RecordUntilEnter(string path)
    {
        var format = new NAudio.Wave.WaveFormat(16000, 16, 1);
        using var input = new NAudio.Wave.WaveInEvent { WaveFormat = format, BufferMilliseconds = 50 };
        using var writer = new NAudio.Wave.WaveFileWriter(path, format);
        input.DataAvailable += (_, e) => writer.Write(e.Buffer, 0, e.BytesRecorded);
        input.StartRecording();
        Console.Write("  ● recording, Enter to stop… ");
        Console.ReadLine();
        input.StopRecording();
        Thread.Sleep(150);
        writer.Flush();
        return writer.TotalTime.TotalSeconds;
    }
}
