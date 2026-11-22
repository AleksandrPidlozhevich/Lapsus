using Lapsus.Core.Speech;
using Lapsus.Speech;

// The app's own recogniser (WhisperRecognizer, VAD, window choice) and its filter, so a sample shows what would be typed.
// Each sample is scaled by --gain and framed by --pad seconds of quiet, as a press-speak-release recording is.
static class AppPath
{
    public static async Task<int> RunAsync(string model, string vad, string samplesDir, string[] args)
    {
        var lang = "uk";
        string[] among = [];
        float gain = 1f;
        double pad = 0.5;
        var gpu = true;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--lang": lang = args[++i]; break;
                case "--among": among = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries); break;
                case "--gain": gain = float.Parse(args[++i]); break;
                case "--pad": pad = double.Parse(args[++i]); break;
                case "--cpu": gpu = false; break;
            }
        }

        using var recognizer = new WhisperRecognizer();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        recognizer.Load(model, vad, gpu);
        await recognizer.WarmUpAsync();
        Console.WriteLine($"{recognizer.DeviceLabel}: loaded in {clock.ElapsedMilliseconds} ms");

        var quiet = new float[(int)(16000 * pad)];
        foreach (var sample in Probe.LoadSamples(samplesDir))
        {
            var pcm = quiet.Concat(sample.Pcm.Select(s => s * gain)).Concat(quiet).ToArray();
            clock.Restart();
            var result = await recognizer.TranscribeAsync(pcm, new SpeechRequest(lang, null, among));
            // DictationEngine's retry: without detection an unsure decode is tried in the next language.
            if (!recognizer.FullWindowIsCheap && result.MeanTokenProbability < 0.75f
                && result.SpeechSeconds >= WhisperRecognizer.OneWord.TotalSeconds
                && among.FirstOrDefault(l => l != result.Language) is { } other)
            {
                var second = await recognizer.TranscribeAsync(pcm, new SpeechRequest(other, null, [other]));
                if (second.MeanTokenProbability > result.MeanTokenProbability)
                    result = second;
            }
            var typed = HallucinationFilter.Clean(result.Text);
            Console.WriteLine($"{sample.Name,-10} {clock.ElapsedMilliseconds,6} ms  speech {result.SpeechSeconds:0.00} s  " +
                              $"{result.Language}  raw \"{result.Text}\"  typed \"{typed}\"  want \"{sample.Reference}\"");
        }
        return 0;
    }
}
