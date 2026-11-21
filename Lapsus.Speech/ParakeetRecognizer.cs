using System.Text;
using System.Text.RegularExpressions;
using Lapsus.Core.Speech;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lapsus.Speech;

// NVIDIA Parakeet TDT (FastConformer encoder, token-and-duration transducer) in the istupakov/onnx-asr export:
// nemo128.onnx turns audio into mel features, the encoder turns those into frames (80 ms each), and decoder_joint
// picks, frame by frame, a token and how many frames to jump. Runs on the ONNX Runtime Lapsus.Neural brings in:
// sherpa-onnx would do the same but ships its own onnxruntime.dll (1.28), which collides with GenAI's (1.23).
// Unlike Whisper it writes nothing over silence, takes no prompt and picks the language itself.
// The same class runs NVIDIA's one-language FastConformer CTC models (a single model.onnx, no decoder): every
// frame's likeliest token, repeats and blanks dropped. Their 80-band preprocessor ships with the app (nemo80.onnx,
// from onnx-asr), since no model repository carries it. They write words over silence and steady noise ("ორ იყო.",
// "Այդ էին։", even at 0.96 confidence), so their folder carries Silero VAD and audio it hears no speech in is not
// shown to them. A level gate cannot do it: FLEURS speech peaks as low as 0.0013 RMS.
public sealed partial class ParakeetRecognizer : ISpeechRecognizer
{
    private const int SampleRate = 16000;
    private const int MaxTokensPerFrame = 10;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Model? _model;
    private bool _disposed;

    public bool IsLoaded => _model is not null && !_disposed;

    public string DeviceLabel => $"CPU, {Threads} threads";

    // An i7-9700F (8 cores, no SMT): 4 threads 280 ms a phrase, 8 threads 475 ms — the encoder stops scaling early.
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    // CPU only: the encoder on DirectML took 562 ms a phrase to the CPU's 280 (RTX 3080, 22 phrases), since the
    // joint runs per token on the CPU anyway and every phrase pays the copies.
    public void Load(string dir)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            _model?.Dispose();
            _model = null;
            _model = new Model(dir, Threads);
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

    private async Task<SpeechResult> TranscribeAsync(
        ReadOnlyMemory<float> pcm, SpeechRequest request, bool checkSpeech, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var model = _model ?? throw new InvalidOperationException("The Parakeet model is not loaded.");
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

    // Past the VAD, or a second of silence would never reach the model.
    public Task WarmUpAsync() => TranscribeAsync(new float[SampleRate], new SpeechRequest("en"), checkSpeech: false, default);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _model?.Dispose();
        _model = null;
    }

    // Greedy TDT decoding as onnx-asr does it: a blank or a jump moves on, and a frame emits at most
    // MaxTokensPerFrame tokens so a confused joint cannot stall. joint(frame, lastToken) returns the token and the
    // jump; accept() is called for each token kept, so the caller can advance the decoder state only then.
    internal static List<int> DecodeTdt(int frames, int blank, Func<int, int, (int Token, int Skip)> joint, Action accept)
    {
        var tokens = new List<int>();
        int t = 0, emitted = 0;
        while (t < frames)
        {
            var (token, skip) = joint(t, tokens.Count > 0 ? tokens[^1] : blank);
            if (token != blank)
            {
                accept();
                tokens.Add(token);
                emitted++;
            }

            if (skip > 0)
            {
                t += skip;
                emitted = 0;
            }
            else if (token == blank || emitted == MaxTokensPerFrame)
            {
                t++;
                emitted = 0;
            }
        }
        return tokens;
    }

    // NVIDIA's Armenian model writes the conjunction and the ligature as "եւ" (the diaspora's spelling); Armenia
    // writes "և", capital "Եվ". Reformed spelling has no other "եւ", so every one is the ligature.
    internal static string Orthography(string text, string language) =>
        language == "hy" ? text.Replace("Եւ", "Եվ").Replace("եւ", "և") : text;

    // Greedy CTC: a token counts where it differs from the frame before and is not blank.
    internal static List<int> DecodeCtc(IEnumerable<int> best, int blank)
    {
        var tokens = new List<int>();
        var previous = blank;
        foreach (var token in best)
        {
            if (token != blank && token != previous)
                tokens.Add(token);
            previous = token;
        }
        return tokens;
    }

    // SentencePiece pieces mark a word start with ▁; a space survives only before a word, not before punctuation.
    internal static string Detokenize(IEnumerable<string> pieces)
    {
        var joined = string.Concat(pieces).Replace('▁', ' ');
        return SpaceRule().Replace(joined, m => m.Groups[1].Success ? " " : "").Trim();
    }

    [GeneratedRegex(@"\A\s|\s\B|(\s)\b")]
    private static partial Regex SpaceRule();

    private sealed class Model : IDisposable
    {
        private readonly InferenceSession _features;
        private readonly InferenceSession _encoder;
        private readonly InferenceSession? _joint;
        private readonly string[] _vocab;
        private readonly int _blank;
        private readonly int[] _stateShape1 = [];
        private readonly int[] _stateShape2 = [];
        private readonly bool _int64Targets;
        private readonly SileroVad? _vad;

        public Model(string dir, int threads)
        {
            _vocab = ReadVocab(Path.Combine(dir, "vocab.txt"));
            _blank = Array.IndexOf(_vocab, "<blk>");
            if (_blank < 0)
                throw new InvalidDataException("vocab.txt has no <blk> token.");

            using var cpu = CpuOptions(threads);
            var ctc = System.IO.File.Exists(File(dir, "model"));
            _encoder = new InferenceSession(File(dir, ctc ? "model" : "encoder-model"), cpu);
            var bands = _encoder.InputMetadata["audio_signal"].Dimensions[1];
            var features = Path.Combine(dir, $"nemo{bands}.onnx");
            _features = new InferenceSession(
                System.IO.File.Exists(features) ? features : Path.Combine(AppContext.BaseDirectory, $"nemo{bands}.onnx"), cpu);
            var vad = Path.Combine(dir, "silero_vad.onnx");
            if (System.IO.File.Exists(vad))
                _vad = new SileroVad(vad, cpu);
            if (ctc)
                return;

            _joint = new InferenceSession(File(dir, "decoder_joint-model"), cpu);

            var inputs = _joint.InputMetadata;
            _stateShape1 = [inputs["input_states_1"].Dimensions[0], 1, inputs["input_states_1"].Dimensions[2]];
            _stateShape2 = [inputs["input_states_2"].Dimensions[0], 1, inputs["input_states_2"].Dimensions[2]];
            _int64Targets = inputs["targets"].ElementDataType == TensorElementType.Int64;
        }

        // Without a VAD in the folder (the Parakeets, which write nothing over silence) everything is speech.
        public bool HearsSpeech(float[] pcm) => _vad?.HasSpeech(pcm) ?? true;

        // Null once cancelled; the caller throws.
        public string? Transcribe(float[] pcm, CancellationToken cancel)
        {
            // Audio → mel features [1, 128, T].
            using var featureOut = _features.Run(
            [
                NamedOnnxValue.CreateFromTensor("waveforms", new DenseTensor<float>(pcm, [1, pcm.Length])),
                NamedOnnxValue.CreateFromTensor("waveforms_lens", new DenseTensor<long>(new[] { (long)pcm.Length }, [1])),
            ]);
            var features = featureOut.First(v => v.Name == "features").AsTensor<float>();
            var featureLens = featureOut.First(v => v.Name == "features_lens").AsTensor<long>();
            if (cancel.IsCancellationRequested)
                return null;

            if (_joint is null)
                return Ctc(features, featureLens);

            // Features → frames [1, D, F].
            using var encoderOut = _encoder.Run(
            [
                NamedOnnxValue.CreateFromTensor("audio_signal", features),
                NamedOnnxValue.CreateFromTensor("length", featureLens),
            ]);
            var encoded = encoderOut.First(v => v.Name == "outputs").AsTensor<float>();
            var frames = (int)Math.Min(encoderOut.First(v => v.Name == "encoded_lengths").AsTensor<long>()[0], encoded.Dimensions[2]);
            var width = encoded.Dimensions[1];
            if (cancel.IsCancellationRequested)
                return null;

            var state1 = new DenseTensor<float>(_stateShape1);
            var state2 = new DenseTensor<float>(_stateShape2);
            DenseTensor<float>? next1 = null, next2 = null;
            var frame = new DenseTensor<float>([1, width, 1]);

            (int, int) Joint(int t, int last)
            {
                // A jump past the last frame ends the decode.
                if (cancel.IsCancellationRequested)
                    return (_blank, frames);
                for (var d = 0; d < width; d++)
                    frame[0, d, 0] = encoded[0, d, t];

                using var output = _joint!.Run(
                [
                    NamedOnnxValue.CreateFromTensor("encoder_outputs", frame),
                    Index("targets", last, [1, 1]),
                    Index("target_length", 1, [1]),
                    NamedOnnxValue.CreateFromTensor("input_states_1", state1),
                    NamedOnnxValue.CreateFromTensor("input_states_2", state2),
                ]);
                var logits = output.First(v => v.Name == "outputs").AsTensor<float>().ToArray();
                next1 = Copy(output.First(v => v.Name == "output_states_1").AsTensor<float>());
                next2 = Copy(output.First(v => v.Name == "output_states_2").AsTensor<float>());
                // The first vocab-size logits are tokens (blank last), the rest are jumps of 0, 1, 2… frames.
                return (ArgMax(logits, 0, _vocab.Length), ArgMax(logits, _vocab.Length, logits.Length - _vocab.Length));
            }

            var tokens = DecodeTdt(frames, _blank, Joint, () => (state1, state2) = (next1!, next2!));
            return Detokenize(tokens.Select(t => _vocab[t]));
        }

        // Features → log-probabilities [1, F, vocab]; frames = (features - 1) / 8 + 1, as onnx-asr counts them.
        private string Ctc(Tensor<float> features, Tensor<long> featureLens)
        {
            using var output = _encoder.Run(
            [
                NamedOnnxValue.CreateFromTensor("audio_signal", features),
                NamedOnnxValue.CreateFromTensor("length", featureLens),
            ]);
            var logprobs = output.First(v => v.Name == "logprobs").AsTensor<float>();
            var frames = (int)Math.Min((featureLens[0] - 1) / 8 + 1, logprobs.Dimensions[1]);
            var width = logprobs.Dimensions[2];
            var values = logprobs.ToArray();
            var best = Enumerable.Range(0, frames).Select(t => ArgMax(values, t * width, width));
            return Detokenize(DecodeCtc(best, _blank).Select(t => _vocab[t]));
        }

        private NamedOnnxValue Index(string name, int value, int[] shape) => _int64Targets
            ? NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(new[] { (long)value }, shape))
            : NamedOnnxValue.CreateFromTensor(name, new DenseTensor<int>(new[] { value }, shape));

        private static DenseTensor<float> Copy(Tensor<float> tensor) =>
            new(tensor.ToArray(), tensor.Dimensions.ToArray());

        private static int ArgMax(float[] values, int start, int count)
        {
            var best = start;
            for (var i = start + 1; i < start + count; i++)
                if (values[i] > values[best])
                    best = i;
            return best - start;
        }

        private static SessionOptions CpuOptions(int threads)
        {
            var options = new SessionOptions
            {
                IntraOpNumThreads = threads,
                InterOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            // Live words run a recognition every 300 ms; spinning threads burn the gaps between them. Off: CPU 3.70 →
            // 1.65 core-seconds per second of speech for +23 ms a recognition (i7-9700F, SpeechProbe `live`).
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
            return options;
        }

        // The int8 export is the one we ship; the full-precision one is used only when it is all that is there.
        private static string File(string dir, string name)
        {
            var int8 = Path.Combine(dir, name + ".int8.onnx");
            return System.IO.File.Exists(int8) ? int8 : Path.Combine(dir, name + ".onnx");
        }

        // "piece id" per line; ids are dense from 0.
        private static string[] ReadVocab(string path)
        {
            var map = new Dictionary<int, string>();
            foreach (var line in System.IO.File.ReadLines(path, Encoding.UTF8))
            {
                var space = line.LastIndexOf(' ');
                if (space > 0 && int.TryParse(line.AsSpan(space + 1), out var id))
                    map[id] = line[..space];
            }
            var vocab = new string[map.Count];
            foreach (var (id, piece) in map)
                vocab[id] = piece;
            return vocab;
        }

        public void Dispose()
        {
            _features.Dispose();
            _encoder.Dispose();
            _joint?.Dispose();
            _vad?.Dispose();
        }
    }
}
