using System.Diagnostics;
using Lapsus.Core.Speech;
using Lapsus.Speech;

// What live words cost: each recording is replayed as the app hears it, its audio so far recognised every
// <every> ms of audio, as DictationSession does within a part. Reports wall time and the process's CPU time
// (which counts threads spinning while idle) per second of speech.
static class Live
{
    public static async Task<int> RunAsync(string modelDir, string samplesDir, string[] args)
    {
        int threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), every = 300, window = int.MaxValue;
        for (var i = 0; i + 1 < args.Length; i += 2)
            switch (args[i])
            {
                case "--threads": threads = int.Parse(args[i + 1]); break;
                case "--every": every = int.Parse(args[i + 1]); break;
                // Only the last this-many ms: the ceiling of what cutting confirmed audio could save.
                case "--window": window = int.Parse(args[i + 1]); break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }

        var samples = Probe.LoadSamples(samplesDir).Where(s => s.Reference is not null).ToList();
        using var recognizer = new ParakeetRecognizer { Threads = threads };
        recognizer.Load(modelDir);
        await recognizer.WarmUpAsync();

        var step = 16 * every;
        double audio = 0, wall = 0;
        var calls = 0;
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpuBefore = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        foreach (var s in samples)
        {
            audio += s.Seconds;
            for (var end = step; end < s.Pcm.Length + step; end += step)
            {
                var sw = Stopwatch.StartNew();
                var to = Math.Min(end, s.Pcm.Length);
                var from = window == int.MaxValue ? 0 : Math.Max(0, to - 16 * window);
                await recognizer.TranscribeAsync(s.Pcm.AsMemory(from, to - from), new SpeechRequest("ru"));
                wall += sw.Elapsed.TotalSeconds;
                calls++;
                // Real time between recognitions: the threads sit idle (or spin) while the user speaks.
                var gap = every / 1000.0 - sw.Elapsed.TotalSeconds;
                if (gap > 0)
                    await Task.Delay(TimeSpan.FromSeconds(gap));
            }
        }
        process.Refresh();
        var cpu = (process.TotalProcessorTime - cpuBefore).TotalSeconds;

        Console.WriteLine($"threads {threads}, every {every} ms: {calls} recognitions over {audio:F1} s of speech");
        Console.WriteLine($"  wall {1000 * wall / calls:F0} ms a recognition; CPU {cpu / audio:F2} core-s per s of speech; " +
                          $"elapsed {clock.Elapsed.TotalSeconds:F1} s");
        return 0;
    }
}
