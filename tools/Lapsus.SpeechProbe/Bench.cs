using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lapsus.Core.Speech;
using Lapsus.Speech;

// Speech accuracy against fixed FLEURS phrases (google/fleurs, CC BY 4.0), like the sweep is for the correction brain:
//   bench pin <lang>...                  choose the phrases and write bench/<lang>.tsv (checked in; the audio is not)
//   bench fetch [<lang>...] [--dir D]    get them from the archive at the pinned commit, every file checked by SHA-256
//   bench run <model> [<lang>...] [--device gpu|cpu] [--name id] [--dir D] [--compare | --write-baseline]
// <model> is a model as the app installs it: a Parakeet/FastConformer folder or a Whisper .bin. --compare exits 2 when
// WER or CER moved more than 0.5 pp the wrong way against bench/baseline/<model>-<device>-<lang>.json; a missing phrase
// set or baseline exits 1, so nothing passes by having nothing to measure.
internal static class Bench
{
    private const string Repo = "google/fleurs";
    private const string Revision = "70bb2e84b976b7e960aa89f1c648e09c59f894dd";
    private const int Phrases = 25;
    private const double Tolerance = 0.5;

    // Lapsus language → FLEURS config.
    private static readonly Dictionary<string, string> Configs = new()
    {
        ["ru"] = "ru_ru", ["uk"] = "uk_ua", ["en"] = "en_us", ["he"] = "he_il", ["ka"] = "ka_ge", ["hy"] = "hy_am",
        ["de"] = "de_de", ["pl"] = "pl_pl", ["be"] = "be_by", ["bg"] = "bg_bg", ["ar"] = "ar_eg", ["ko"] = "ko_kr",
    };

    private static readonly string Here = Path.Combine(FindRepo(), "tools", "Lapsus.SpeechProbe", "bench");

    private static readonly string DefaultDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus", "speech-bench");

    private sealed record Entry(string File, string Sha256, string Text);

    private sealed record Result(string Model, string Device, string Language, int Phrases, double Wer, double Cer, double MsPerPhrase);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
            return Fail("bench pin|fetch|run …");

        var dir = Option(args, "--dir") ?? DefaultDir;
        var rest = Positional(args[1..]);
        switch (args[0])
        {
            case "pin":
                foreach (var lang in rest)
                    await PinAsync(lang);
                return 0;
            case "fetch":
                foreach (var lang in rest.Length > 0 ? rest : Pinned())
                    await FetchAsync(lang, dir);
                return 0;
            case "run" when rest.Length > 0:
                return await MeasureAsync(rest[0], Option(args, "--name"), rest.Length > 1 ? rest[1..] : Pinned(), dir,
                    Option(args, "--device") ?? "gpu", args.Contains("--compare"), args.Contains("--write-baseline"));
            default:
                return Fail("bench pin|fetch|run …");
        }
    }

    // The first Phrases recordings in the archive, one per sentence, so 25 phrases are 25 different texts.
    private static async Task PinAsync(string lang)
    {
        var texts = await TranscriptsAsync(lang);
        var chosen = new List<Entry>();
        var sentences = new HashSet<string>();
        await foreach (var (name, wav) in WavsAsync(lang, _ => chosen.Count < Phrases))
            if (texts.TryGetValue(name, out var t) && sentences.Add(t.Id))
                chosen.Add(new Entry(name, Sha(wav), t.Text));

        Directory.CreateDirectory(Here);
        await File.WriteAllLinesAsync(Path.Combine(Here, $"{lang}.tsv"),
            [$"# {Repo}@{Revision} data/{Configs[lang]}: file, SHA-256 of the 16-bit wav bench fetch writes, transcription",
             .. chosen.Select(e => $"{e.File}\t{e.Sha256}\t{e.Text}")]);
        Console.WriteLine($"{lang}: pinned {chosen.Count}");
    }

    private static async Task FetchAsync(string lang, string dir)
    {
        var wanted = Manifest(lang).ToDictionary(e => e.File);
        var target = Path.Combine(dir, lang);
        Directory.CreateDirectory(target);
        var missing = wanted.Keys.Where(f => !File.Exists(Path.Combine(target, f))).ToHashSet();
        await foreach (var (name, wav) in WavsAsync(lang, _ => missing.Count > 0))
        {
            if (!missing.Remove(name))
                continue;
            if (Sha(wav) != wanted[name].Sha256)
                throw new InvalidDataException($"{lang}/{name}: SHA-256 does not match the manifest");
            await File.WriteAllBytesAsync(Path.Combine(target, name), wav);
            await File.WriteAllTextAsync(Path.Combine(target, Path.ChangeExtension(name, ".txt")), wanted[name].Text);
        }
        if (missing.Count > 0)
            throw new InvalidDataException($"{lang}: {missing.Count} files not in the archive");
        Console.WriteLine($"{lang}: {wanted.Count} phrases in {target}");
    }

    private static async Task<int> MeasureAsync(
        string modelPath, string? name, string[] langs, string dir, string device, bool compare, bool writeBaseline)
    {
        var nemo = Directory.Exists(modelPath);
        // The catalog id: the app keeps each model in a folder named after it.
        var model = name ?? (nemo ? Path.GetFileName(Path.TrimEndingDirectorySeparator(modelPath))
            : Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(modelPath)))!);
        if (nemo)
            device = "cpu";

        ISpeechRecognizer recognizer;
        if (nemo)
        {
            var parakeet = new NemoRecognizer();
            parakeet.Load(modelPath);
            await parakeet.WarmUpAsync();
            recognizer = parakeet;
        }
        else
        {
            var vad = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(modelPath)))!, "vad",
                "ggml-silero-v6.2.0.bin");
            var whisper = new WhisperRecognizer();
            whisper.Load(modelPath, File.Exists(vad) ? vad : null, device == "gpu");
            await whisper.WarmUpAsync();
            recognizer = whisper;
        }
        using var _ = recognizer;

        var failed = false;
        foreach (var lang in langs)
        {
            var entries = Manifest(lang);
            var folder = Path.Combine(dir, lang);
            if (entries.Any(e => !File.Exists(Path.Combine(folder, e.File))))
                return Fail($"{lang}: phrases missing in {folder}; run bench fetch {lang}");

            double words = 0, wordErrors = 0, chars = 0, charErrors = 0, ms = 0;
            foreach (var entry in entries)
            {
                var pcm = Pcm(await File.ReadAllBytesAsync(Path.Combine(folder, entry.File)));
                var clock = Stopwatch.StartNew();
                var heard = await recognizer.TranscribeAsync(pcm, new SpeechRequest(lang, null, [lang]));
                ms += clock.ElapsedMilliseconds;
                var (rw, hw) = (Probe.Words(entry.Text), Probe.Words(HallucinationFilter.Clean(heard.Text)));
                words += rw.Length;
                wordErrors += Probe.Distance(rw, hw);
                var (rc, hc) = (string.Join(' ', rw).ToCharArray(), string.Join(' ', hw).ToCharArray());
                chars += rc.Length;
                charErrors += Probe.Distance(rc, hc);
            }

            var result = new Result(model, device, lang, entries.Count,
                Math.Round(100 * wordErrors / words, 2), Math.Round(100 * charErrors / chars, 2),
                Math.Round(ms / entries.Count));
            var baselinePath = Path.Combine(Here, "baseline", $"{model}-{device}-{lang}.json");
            var line = $"{model} {device} {lang}: WER {result.Wer:F1}%  CER {result.Cer:F1}%  {result.MsPerPhrase:F0} ms a phrase";

            if (writeBaseline)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
                await File.WriteAllTextAsync(baselinePath,
                    JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
                Console.WriteLine($"{line}  (baseline written)");
            }
            else if (compare)
            {
                if (!File.Exists(baselinePath))
                    return Fail($"{line}\n  no baseline at {baselinePath}");
                var was = JsonSerializer.Deserialize<Result>(await File.ReadAllTextAsync(baselinePath))!;
                var worse = result.Wer - was.Wer > Tolerance || result.Cer - was.Cer > Tolerance;
                failed |= worse;
                Console.WriteLine($"{line}  (baseline WER {was.Wer:F1}%  CER {was.Cer:F1}%){(worse ? "  REGRESSION" : "")}");
            }
            else
                Console.WriteLine(line);
        }
        return failed ? 2 : 0;
    }

    // test.tsv: id, file, raw transcription, …
    private static async Task<Dictionary<string, (string Id, string Text)>> TranscriptsAsync(string lang)
    {
        using var http = new HttpClient();
        var tsv = await http.GetStringAsync(Url($"data/{Configs[lang]}/test.tsv"));
        return tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t'))
            .Where(p => p.Length > 2)
            .GroupBy(p => p[1])
            .ToDictionary(g => g.Key, g => (g.First()[0], g.First()[2]));
    }

    // The archive is read as a stream and dropped as soon as more() says enough: 25 phrases cost a few MB of its 500.
    private static async IAsyncEnumerable<(string Name, byte[] Wav)> WavsAsync(string lang, Func<int, bool> more)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        await using var stream = await http.GetStreamAsync(Url($"data/{Configs[lang]}/audio/test.tar.gz"));
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var seen = 0;
        while (more(seen) && await tar.GetNextEntryAsync() is { } entry)
        {
            if (entry.DataStream is null || !entry.Name.EndsWith(".wav", StringComparison.Ordinal))
                continue;
            using var buffer = new MemoryStream();
            await entry.DataStream.CopyToAsync(buffer);
            seen++;
            yield return (Path.GetFileName(entry.Name), ToPcm16(buffer.ToArray()));
        }
    }

    // FLEURS stores 32-bit float; Whisper.net and the rest of the probe read 16-bit PCM.
    internal static byte[] ToPcm16(byte[] wav)
    {
        var (format, data) = Chunks(wav);
        if (format != 3)
            return wav;
        var samples = new short[data.Count / 4];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)Math.Clamp(BitConverter.ToSingle(data.AsSpan(i * 4, 4)) * 32767f, short.MinValue, short.MaxValue);

        using var output = new MemoryStream();
        using var w = new BinaryWriter(output);
        w.Write("RIFF"u8);
        w.Write(36 + samples.Length * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(16000);
        w.Write(32000);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples.Length * 2);
        foreach (var s in samples)
            w.Write(s);
        return output.ToArray();
    }

    private static float[] Pcm(byte[] wav)
    {
        var (_, data) = Chunks(wav);
        var pcm = new float[data.Count / 2];
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = BitConverter.ToInt16(data.AsSpan(i * 2, 2)) / 32768f;
        return pcm;
    }

    private static (int Format, ArraySegment<byte> Data) Chunks(byte[] wav)
    {
        var format = 0;
        for (var at = 12; at + 8 <= wav.Length;)
        {
            var id = Encoding.ASCII.GetString(wav, at, 4);
            var size = BitConverter.ToInt32(wav, at + 4);
            if (id == "fmt ")
                format = BitConverter.ToInt16(wav, at + 8);
            if (id == "data")
                return (format, new ArraySegment<byte>(wav, at + 8, Math.Min(size, wav.Length - at - 8)));
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException("no data chunk");
    }

    private static List<Entry> Manifest(string lang)
    {
        var path = Path.Combine(Here, $"{lang}.tsv");
        if (!File.Exists(path))
            throw new FileNotFoundException($"no phrase set for {lang}; bench pin {lang}", path);
        return File.ReadLines(path).Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('\t')).Select(p => new Entry(p[0], p[1], p[2])).ToList();
    }

    private static string[] Pinned() =>
        Directory.Exists(Here) ? Directory.GetFiles(Here, "*.tsv").Select(Path.GetFileNameWithoutExtension).Order().ToArray()! : [];

    private static string Url(string path) => $"https://huggingface.co/datasets/{Repo}/resolve/{Revision}/{path}";

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static string? Option(string[] args, string name) =>
        Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

    private static string[] Positional(string[] args)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Length; i++)
            if (args[i] is "--dir" or "--device" or "--name")
                i++;
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
                list.Add(args[i]);
        return list.ToArray();
    }

    private static string FindRepo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Lapsus.slnx")))
                return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
