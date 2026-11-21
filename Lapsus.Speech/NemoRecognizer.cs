using Lapsus.Core.Speech;

namespace Lapsus.Speech;

// NVIDIA's NeMo speech models (NemoModel): Parakeet TDT and the one-language FastConformer CTCs, in the
// istupakov/onnx-asr export, on the ONNX Runtime Lapsus.Neural brings in — sherpa-onnx would do the same but ships its
// own onnxruntime.dll (1.28), which collides with GenAI's (1.23). Unlike Whisper they take no prompt and pick the
// language themselves. This class owns the model's lifetime and lets one recognition run at a time.
public sealed class NemoRecognizer : ISpeechRecognizer
{
    private const int SampleRate = 16000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private NemoModel? _model;
    private bool _disposed;

    public bool IsLoaded => _model is not null && !_disposed;

    public string DeviceLabel => $"CPU, {Threads} threads";

    // An i7-9700F (8 cores, no SMT): 4 threads 280 ms a phrase, 8 threads 475 ms — the encoder stops scaling early.
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    public void Load(string dir)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            _model?.Dispose();
            _model = null;
            _model = NemoModel.Load(dir, Threads);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Unload()
    {
        _gate.Wait();
        try
        {
            _model?.Dispose();
            _model = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<SpeechResult> TranscribeAsync(
        ReadOnlyMemory<float> pcm, SpeechRequest request, CancellationToken cancellationToken = default) =>
        TranscribeAsync(pcm, request, checkSpeech: true, cancellationToken);

    // Past the VAD, or a second of silence would never reach the model.
    public Task WarmUpAsync() => TranscribeAsync(new float[SampleRate], new SpeechRequest("en"), checkSpeech: false, default);

    private async Task<SpeechResult> TranscribeAsync(
        ReadOnlyMemory<float> pcm, SpeechRequest request, bool checkSpeech, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var model = _model ?? throw new InvalidOperationException("The speech model is not loaded.");
            // A tenth of a second holds no word; the preprocessor rejects much shorter input outright.
            if (pcm.Length < SampleRate / 10)
                return SpeechResult.Silence(request.Language);

            var samples = pcm.ToArray();
            var text = await Task.Run(
                    () => checkSpeech && !model.HearsSpeech(samples) ? "" : model.Transcribe(samples, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            // Thrown here, not on the worker: a throw inside Task.Run's delegate stops the debugger as unhandled
            // although DictationSession catches it (a dropped early part, Esc).
            cancellationToken.ThrowIfCancellationRequested();
            return text!.Length == 0
                ? SpeechResult.Silence(request.Language)
                : new SpeechResult(Orthography(text, request.Language), request.Language, samples.Length / (double)SampleRate);
        }
        finally
        {
            _gate.Release();
        }
    }

    // NVIDIA's Armenian model writes the conjunction and the ligature as "եւ" (the diaspora's spelling); Armenia
    // writes "և", capital "Եվ". Reformed spelling has no other "եւ", so every one is the ligature.
    internal static string Orthography(string text, string language) =>
        language == "hy" ? text.Replace("Եւ", "Եվ").Replace("եւ", "և") : text;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _model?.Dispose();
        _model = null;
    }
}
