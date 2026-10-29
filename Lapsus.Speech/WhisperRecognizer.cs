using Lapsus.Core.Speech;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace Lapsus.Speech;

// Whisper (ggml model through Whisper.net) as the dictation recognizer. Decoding follows what stage 1 measured
// on large-v3-turbo:
//  - GPU: the full 30 s window, greedy, with the prompt — best quality and ~0.3 s a phrase;
//  - CPU: the window cut to the audio's length, which is five times faster, with beam search, because greedy
//    decoding on a cut window falls into loops.
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

    // Whether decoding runs on a GPU. Read from the native log while loading: whisper.cpp falls back to the CPU
    // quietly when asked for a GPU it does not find.
    public bool OnGpu { get; private set; }

    public string DeviceLabel { get; private set; } = "";

    // whisper.cpp's own log from the last load, for diagnostics.
    public IReadOnlyList<string> LoadLog { get; private set; } = [];

    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount, 1, 8);

    // modelPath: a ggml Whisper model. vadPath: a ggml Silero VAD model, or null to decode audio uncut.
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
                factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = preferGpu });
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

        var (onGpu, card) = UsedGpu(log);
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
            DeviceLabel = onGpu ? $"GPU ({card ?? RuntimeOptions.LoadedLibrary?.ToString()})" : "CPU";
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
        if (OnGpu)
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
        // GPU, ~10 s on a CPU. On a CPU the dictation stays in the main language.
        var probability = 1f;
        if (OnGpu && request.DetectAmong is { Count: > 1 } among)
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
        var minP = 1f;
        await foreach (var segment in processor.ProcessAsync(speech, ct).ConfigureAwait(false))
        {
            text.Append(segment.Text);
            minP = Math.Min(minP, segment.MinProbability);
        }

        return new SpeechResult(text.ToString().Trim(), language, seconds, probability, minP);
    }

    // The encoder window for audio this long on the CPU: a second of slack, rounded up to 64 frames, never so
    // small that Whisper loses its footing; null for audio that needs the whole window anyway.
    internal static int? WindowFor(double seconds)
    {
        var frames = (int)Math.Ceiling((seconds + 1.0) * FramesPerSecond / 64.0) * 64;
        return frames >= FullWindowFrames ? null : Math.Max(frames, 384);
    }

    // Speech stretches as VAD finds them, joined.
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
    // "CPU total size" without a GPU), and the Vulkan backend names the card it found.
    private static (bool OnGpu, string? Card) UsedGpu(List<string> log)
    {
        lock (log)
        {
            var onGpu = log.Select(line => ModelBackend().Match(line))
                .Any(m => m.Success && !m.Groups[1].Value.StartsWith("CPU", StringComparison.OrdinalIgnoreCase));
            var card = log.Select(line => VulkanCard().Match(line)).FirstOrDefault(m => m.Success)?.Groups[1].Value.Trim();
            return (onGpu, card);
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"whisper_model_load:\s+(\S+)\s+total size")]
    private static partial System.Text.RegularExpressions.Regex ModelBackend();

    [System.Text.RegularExpressions.GeneratedRegex(@"ggml_vulkan: 0 = ([^|(]+)")]
    private static partial System.Text.RegularExpressions.Regex VulkanCard();

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
