using Lapsus.Core.Speech;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace Lapsus.Speech;

// Decoding follows what stage 1 measured on large-v3-turbo:
//  - GPU: the full 30 s window, greedy, with the prompt — best quality and ~0.3 s a phrase;
//  - CPU, and Metal: the window cut to the audio's length, with beam search, because greedy decoding on a cut
//    window falls into loops. On an M1 the full window took 5-6 s a pass (11-14 s for one word with language
//    detection); cut, a phrase takes ~1.4 s at the same accuracy.
// Silence is cut with Silero VAD before decoding; without it Whisper writes subtitle credits over silence.
public sealed partial class WhisperRecognizer : ISpeechRecognizer
{
    private const int SampleRate = 16000;

    // Less speech than this after VAD is a cough or a click, not a phrase.
    public static readonly TimeSpan MinSpeech = TimeSpan.FromMilliseconds(300);

    // Whisper encodes 50 frames per second of a 30 s window.
    private const int FramesPerSecond = 50;
    private const int FullWindowFrames = 1500;

    private static readonly object RuntimeGate = new();
    private static bool _runtimeChosen;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private CancellationTokenSource _runCts = new();
    private WhisperFactory? _factory;
    private WhisperVadFactory? _vadFactory;
    private WhisperVadProcessor? _vad;
    private bool _disposed;

    public bool IsLoaded
    {
        get
        {
            lock (_gate)
                return !_disposed && _factory is not null;
        }
    }

    // Read from the native log while loading: whisper.cpp falls back to the CPU quietly when asked for a GPU it does not find.
    public bool OnGpu { get; private set; }

    // A GPU that runs the full 30 s window in a fraction of a second (Vulkan, CUDA); Metal does not.
    public bool FullWindowIsCheap { get; private set; }

    public string DeviceLabel { get; private set; } = "";

    public IReadOnlyList<string> LoadLog { get; private set; } = [];

    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount, 1, 8);

    // GPU only: RTX 3080 544 → 459 ms a phrase, WER 9.3 → 8.7%. On a CPU it is faster too but writes more
    // false Latin (6 → 10 of 22 phrases), so a CPU load leaves it off.
    public bool FlashAttention { get; init; } = true;

    public void Load(string modelPath, string? vadPath, bool preferGpu)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Unload();
        ChooseRuntime();

        var log = new List<string>();
        WhisperFactory? factory = null;
        WhisperVadFactory? vadFactory = null;
        WhisperVadProcessor? vad = null;
        try
        {
            using (LogProvider.AddLogger((_, line) =>
                   {
                       lock (log)
                           log.Add(line ?? "");
                   }))
            {
                factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = preferGpu, UseFlashAttention = FlashAttention && preferGpu });
                // The weights load with the first processor; build one now so a dictation never waits for it.
                using (factory.CreateBuilder().WithLanguage("en").Build())
                {
                }
            }

            if (vadPath is not null)
            {
                vadFactory = WhisperVadFactory.FromPath(vadPath);
                vad = vadFactory.CreateBuilder()
                    .WithThreshold(0.5f)
                    .WithMinSilenceDuration(TimeSpan.FromMilliseconds(300))
                    .WithSpeechPadding(TimeSpan.FromMilliseconds(200))
                    .Build();
            }
        }
        catch
        {
            vad?.Dispose();
            vadFactory?.Dispose();
            factory?.Dispose();
            throw;
        }

        var (onGpu, device) = UsedGpu(log);
        onGpu &= preferGpu;
        LoadLog = log;
        lock (_gate)
        {
            if (_disposed)
            {
                vad?.Dispose();
                vadFactory?.Dispose();
                factory?.Dispose();
                throw new ObjectDisposedException(nameof(WhisperRecognizer));
            }

            _factory = factory;
            _vadFactory = vadFactory;
            _vad = vad;
            OnGpu = onGpu;
            FullWindowIsCheap = onGpu && !OperatingSystem.IsMacOS();
            DeviceLabel = DescribeDevice(onGpu, device);
        }
    }

    public void Unload()
    {
        WhisperFactory? factory;
        WhisperVadFactory? vadFactory;
        WhisperVadProcessor? vad;
        CancellationTokenSource old;
        lock (_gate)
        {
            factory = _factory;
            vadFactory = _vadFactory;
            vad = _vad;
            _factory = null;
            _vadFactory = null;
            _vad = null;
            old = _runCts;
            _runCts = new CancellationTokenSource();
            OnGpu = false;
            FullWindowIsCheap = false;
            DeviceLabel = "";
        }

        old.Cancel();
        // A decode in progress holds the run gate; native memory may only go once it has stopped. One that does
        // not stop in time keeps its model: leaking it beats freeing memory a native thread still reads.
        var stopped = _runGate.Wait(TimeSpan.FromSeconds(20));
        try
        {
            if (stopped)
            {
                vad?.Dispose();
                vadFactory?.Dispose();
                factory?.Dispose();
            }
        }
        finally
        {
            if (stopped)
                _runGate.Release();
            old.Dispose();
        }
    }

    public async Task<SpeechResult> TranscribeAsync(
        ReadOnlyMemory<float> pcm, SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WhisperFactory factory;
        WhisperVadProcessor? vad;
        CancellationToken runToken;
        lock (_gate)
        {
            factory = _factory ?? throw new InvalidOperationException("No Whisper model is loaded.");
            vad = _vad;
            runToken = _runCts.Token;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runToken);
        var ct = linked.Token;
        await _runGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Decode(factory, vad, pcm, request, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            _runGate.Release();
        }
    }

    // Runs one second of quiet speech-free audio through the decoder, uncut, so the GPU compiles its shaders
    // now and not on the first dictation (stage 1: 22 s the first time on a machine, then cached by the driver).
    public Task WarmUpAsync(CancellationToken cancellationToken = default) =>
        TranscribeAsync(new float[SampleRate], new SpeechRequest("en"), cancellationToken);

    private async Task<SpeechResult> Decode(
        WhisperFactory factory, WhisperVadProcessor? vad, ReadOnlyMemory<float> pcm, SpeechRequest request,
        CancellationToken ct)
    {
        var language = DictationLanguagePicker.Normalize(request.Language);
        var speech = vad is null || pcm.Length <= SampleRate ? pcm.ToArray() : KeepSpeech(vad, pcm.ToArray());
        var seconds = speech.Length / (double)SampleRate;
        if (vad is not null && pcm.Length > SampleRate && seconds < MinSpeech.TotalSeconds)
            return SpeechResult.Silence(language);
        ct.ThrowIfCancellationRequested();

        var builder = factory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(Threads)
            .WithNoContext()
            .WithSingleSegment()
            .WithProbabilities();
        if (!string.IsNullOrWhiteSpace(request.Prompt))
            builder = builder.WithPrompt(request.Prompt);
        // A window cut to the phrase loses accuracy on a fast GPU even with beam 2-3 (12% / 9.8% WER against 9.3%)
        // for ~10% speed, so such a GPU keeps the full window.
        if (FullWindowIsCheap)
        {
            builder = builder.WithGreedySamplingStrategy(_ => { });
        }
        else
        {
            builder = builder.WithBeamSearchSamplingStrategy(b => b.WithBeamSize(5));
            if (WindowFor(seconds) is { } frames)
                builder = builder.WithAudioContextSize(frames);
        }

        await using var processor = builder.Build();

        // Detection is a pass of its own over the full 30 s window, whatever window decoding uses: ~0.25 s on a
        // fast GPU, ~5 s on Metal, ~10 s on a CPU. Without a fast GPU the dictation stays in the main language.
        var probability = 1f;
        if (FullWindowIsCheap && request.DetectAmong is { Count: > 1 } among)
        {
            var (detected, p) = processor.DetectLanguageWithProbability(speech, among.ToArray());
            var chosen = DictationLanguagePicker.Resolve(language, detected, p);
            probability = p;
            if (chosen != language)
            {
                processor.ChangeLanguage(chosen);
                language = chosen;
            }
        }

        var text = new System.Text.StringBuilder();
        float minP = 1f, sumP = 0f;
        var segments = 0;
        await foreach (var segment in processor.ProcessAsync(speech, ct).ConfigureAwait(false))
        {
            text.Append(segment.Text);
            minP = Math.Min(minP, segment.MinProbability);
            sumP += segment.Probability;
            segments++;
        }

        return new SpeechResult(text.ToString().Trim(), language, seconds, probability, minP,
            segments > 0 ? sumP / segments : 1f);
    }

    // Never so small that Whisper loses its footing. A single word in a 7.7 s window came out as subtitle credits
    // ("Дякую за перегляд!", "Субтитры делал…") in 4 of 10 words on an M1; 10 s read all ten as well as 15 s did,
    // in 1.5 s against 2.3. A phrase keeps the smaller window: 15 s cost it 2.5x the time and wrote "pull request"
    // as "пул реквест".
    internal static int? WindowFor(double seconds)
    {
        if (seconds < OneWord.TotalSeconds)
            return OneWordFrames;
        var frames = (int)Math.Ceiling((seconds + 1.0) * FramesPerSecond / 64.0) * 64;
        return frames >= FullWindowFrames ? null : Math.Max(frames, 384);
    }

    // Speech shorter than this (after VAD) is a word or two.
    public static readonly TimeSpan OneWord = TimeSpan.FromSeconds(1.5);
    private const int OneWordFrames = 512;

    private static float[] KeepSpeech(WhisperVadProcessor vad, float[] pcm)
    {
        var kept = new List<float>(pcm.Length);
        foreach (var segment in vad.DetectSpeech(pcm))
        {
            var from = Math.Clamp((int)(segment.Start.TotalSeconds * SampleRate), 0, pcm.Length);
            var to = Math.Clamp((int)(segment.End.TotalSeconds * SampleRate), from, pcm.Length);
            kept.AddRange(new ArraySegment<float>(pcm, from, to - from));
        }
        return kept.ToArray();
    }

    // whisper.cpp names the backend that holds the weights ("whisper_model_load: Vulkan0 total size = 873 MB",
    // "MTL0" for Metal, "CPU total size" without a GPU). Vulkan and Metal each name the card they found; the
    // runtime package is no name for it: on macOS the package called Cpu is the one that carries Metal.
    internal static (bool OnGpu, string? Device) UsedGpu(IReadOnlyList<string> log)
    {
        lock (log)
        {
            var backend = log.Select(line => ModelBackend().Match(line))
                .FirstOrDefault(m => m.Success && !m.Groups[1].Value.StartsWith("CPU", StringComparison.OrdinalIgnoreCase))
                ?.Groups[1].Value;
            if (backend is null)
                return (false, null);

            var card = log.Select(line => GpuName().Match(line)).FirstOrDefault(m => m.Success) is { } found
                ? CardName(found)
                : null;
            return (true, string.IsNullOrEmpty(card) ? backend : card);
        }
    }

    // Newer ggml prints the device id first ("GPU name:   MTL0 (Apple M2 Pro)"), older the chip alone.
    private static string CardName(System.Text.RegularExpressions.Match found)
    {
        if (found.Groups["vulkan"].Success)
            return found.Groups["vulkan"].Value.Trim();

        var name = found.Groups["metal"].Value.Trim();
        var detail = found.Groups["detail"].Value.Trim();
        return MetalDeviceId().IsMatch(name) && detail.Length > 0 ? detail : name;
    }

    internal static string DescribeDevice(bool onGpu, string? device) =>
        onGpu ? $"GPU ({device})" : "CPU";

    [System.Text.RegularExpressions.GeneratedRegex(@"whisper_model_load:\s+(\S+)\s+total size")]
    private static partial System.Text.RegularExpressions.Regex ModelBackend();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"ggml_vulkan: 0 = (?<vulkan>[^|(]+)|GPU name:\s+(?<metal>[^(\r\n]+)(?:\((?<detail>[^)]*)\))?")]
    private static partial System.Text.RegularExpressions.Regex GpuName();

    [System.Text.RegularExpressions.GeneratedRegex(@"^MTL\d+$")]
    private static partial System.Text.RegularExpressions.Regex MetalDeviceId();

    private static void ChooseRuntime()
    {
        lock (RuntimeGate)
        {
            if (_runtimeChosen)
                return;
            if (OperatingSystem.IsWindows())
                RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];
            _runtimeChosen = true;
        }
    }

    public void AbandonForShutdown()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            cts = _runCts;
        }
        cts.Cancel();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        Unload();
        _runGate.Dispose();
    }
}
