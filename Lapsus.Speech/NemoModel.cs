using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lapsus.Speech;

// One NVIDIA NeMo model in onnx-asr's export, as NemoRecognizer runs it: a preprocessor turns audio into mel
// features, an encoder turns those into frames, and a decoder turns frames into SentencePiece pieces. The folder says
// which: decoder_joint-model is a TDT Parakeet, a lone model.onnx a FastConformer CTC. The CTCs' 80-band preprocessor
// ships with the app (nemo80.onnx, from onnx-asr), since no model repository carries it; they write words over
// silence and steady noise ("ორ იყო.", "Այդ էին։", even at 0.96 confidence), so their folder carries Silero VAD and
// audio it hears no speech in is not shown to them — a level gate cannot do it, FLEURS speech peaks as low as 0.0013
// RMS. The Parakeets write nothing over silence and get no VAD. The encoder is ~93% of the
// time (Parakeet v3, 25 FLEURS ru phrases: 650 ms of 700 a phrase, the joint ~35); on DirectML it took ~480 ms
// whatever the length, so even a short phrase paid that, and it stays on the CPU.
internal abstract partial class NemoModel : IDisposable
{
    private const int MaxTokensPerFrame = 10;

    private readonly InferenceSession _features;
    private readonly SileroVad? _vad;

    protected readonly InferenceSession Encoder;
    protected readonly string[] Vocab;
    protected readonly int Blank;

    protected NemoModel(string dir, InferenceSession encoder, SessionOptions options)
    {
        Encoder = encoder;
        Vocab = ReadVocab(Path.Combine(dir, "vocab.txt"));
        Blank = Array.IndexOf(Vocab, "<blk>");
        if (Blank < 0)
            throw new InvalidDataException("vocab.txt has no <blk> token.");

        // 128 bands for the Parakeets (nemo128.onnx in their folder), 80 for the CTCs (nemo80.onnx beside the app).
        var bands = Encoder.InputMetadata["audio_signal"].Dimensions[1];
        var features = Path.Combine(dir, $"nemo{bands}.onnx");
        _features = new InferenceSession(
            File.Exists(features) ? features : Path.Combine(AppContext.BaseDirectory, $"nemo{bands}.onnx"), options);
        var vad = Path.Combine(dir, "silero_vad.onnx");
        if (File.Exists(vad))
            _vad = new SileroVad(vad, options);
    }

    public static NemoModel Load(string dir, int threads)
    {
        using var options = CpuOptions(threads);
        return File.Exists(Model(dir, "model"))
            ? new CtcModel(dir, new InferenceSession(Model(dir, "model"), options), options)
            : new TdtModel(dir, new InferenceSession(Model(dir, "encoder-model"), options), options);
    }

    // Without a VAD in the folder (the Parakeets, which write nothing over silence) everything is speech.
    public bool HearsSpeech(float[] pcm) => _vad?.HasSpeech(pcm) ?? true;

    // Null once cancelled; the caller throws.
    public string? Transcribe(float[] pcm, CancellationToken cancel)
    {
        using var featureOut = _features.Run(
        [
            NamedOnnxValue.CreateFromTensor("waveforms", new DenseTensor<float>(pcm, [1, pcm.Length])),
            NamedOnnxValue.CreateFromTensor("waveforms_lens", new DenseTensor<long>(new[] { (long)pcm.Length }, [1])),
        ]);
        if (cancel.IsCancellationRequested)
            return null;

        using var encoderOut = Encoder.Run(
        [
            NamedOnnxValue.CreateFromTensor("audio_signal", Output(featureOut, "features")),
            NamedOnnxValue.CreateFromTensor("length", Output<long>(featureOut, "features_lens")),
        ]);
        if (cancel.IsCancellationRequested)
            return null;

        var tokens = Decode(encoderOut, Output<long>(featureOut, "features_lens")[0], cancel);
        return tokens is null ? null : Detokenize(tokens.Select(t => Vocab[t]));
    }

    protected abstract List<int>? Decode(
        IReadOnlyCollection<DisposableNamedOnnxValue> encoderOut, long featureFrames, CancellationToken cancel);

    protected static DenseTensor<float> Output(IReadOnlyCollection<DisposableNamedOnnxValue> values, string name) =>
        Output<float>(values, name);

    protected static DenseTensor<T> Output<T>(IReadOnlyCollection<DisposableNamedOnnxValue> values, string name)
    {
        foreach (var value in values)
            if (value.Name == name)
                return (DenseTensor<T>)value.AsTensor<T>();
        throw new InvalidDataException($"The model has no {name} output.");
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

    internal static int ArgMax(ReadOnlySpan<float> values)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
            if (values[i] > values[best])
                best = i;
        return best;
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
    protected static string Model(string dir, string name)
    {
        var int8 = Path.Combine(dir, name + ".int8.onnx");
        return File.Exists(int8) ? int8 : Path.Combine(dir, name + ".onnx");
    }

    // "piece id" per line; ids are dense from 0.
    private static string[] ReadVocab(string path)
    {
        var map = new Dictionary<int, string>();
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
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

    public virtual void Dispose()
    {
        _features.Dispose();
        Encoder.Dispose();
        _vad?.Dispose();
    }
}

// FastConformer CTC: every frame's likeliest piece, repeats and blanks dropped.
internal sealed class CtcModel(string dir, InferenceSession encoder, SessionOptions options)
    : NemoModel(dir, encoder, options)
{
    // Log-probabilities [1, F, vocab]; frames = (features - 1) / 8 + 1, as onnx-asr counts them.
    protected override List<int>? Decode(
        IReadOnlyCollection<DisposableNamedOnnxValue> encoderOut, long featureFrames, CancellationToken cancel)
    {
        var logprobs = Output(encoderOut, "logprobs");
        var frames = (int)Math.Min((featureFrames - 1) / 8 + 1, logprobs.Dimensions[1]);
        var width = logprobs.Dimensions[2];
        var values = logprobs.Buffer;
        var best = new int[frames];
        for (var t = 0; t < frames; t++)
            best[t] = ArgMax(values.Span.Slice(t * width, width));
        return DecodeCtc(best, Blank);
    }
}

// Parakeet TDT: decoder_joint picks, frame by frame, a piece and how many frames to jump. It runs once per step —
// 150–300 times a phrase — so every buffer it reads and writes is allocated once per model and bound as an OrtValue:
// a step copies one frame in and reads the logits out, and the decoder state flips between two buffer pairs.
internal sealed class TdtModel : NemoModel
{
    private static readonly string[] Inputs = ["encoder_outputs", "targets", "target_length", "input_states_1", "input_states_2"];
    private static readonly string[] Outputs = ["outputs", "output_states_1", "output_states_2"];

    private readonly InferenceSession _joint;
    private readonly RunOptions _run = new();
    private readonly int _width;
    private readonly float[] _frame;
    private readonly float[] _logits;
    private readonly long[] _target64 = [0];
    private readonly int[] _target32 = [0];
    private readonly float[][] _states;
    private readonly OrtValue[] _inputs, _swappedInputs, _outputs, _swappedOutputs;
    private readonly List<IDisposable> _owned = [];

    public TdtModel(string dir, InferenceSession encoder, SessionOptions options) : base(dir, encoder, options)
    {
        _joint = new InferenceSession(Model(dir, "decoder_joint-model"), options);
        var inputs = _joint.InputMetadata;
        _width = inputs["encoder_outputs"].Dimensions[1];
        long[] Shape(string name) => inputs[name].Dimensions.Select(d => d < 0 ? 1L : d).ToArray();
        var stateShapes = new[] { Shape("input_states_1"), Shape("input_states_2") };
        _states = [.. Enumerable.Range(0, 4).Select(i => new float[stateShapes[i % 2].Aggregate(1L, (a, b) => a * b)])];
        // [1, 1, 1, vocab + jumps]: the first vocab-size logits are pieces (blank last), the rest jumps of 0, 1, 2… frames.
        var logitShape = _joint.OutputMetadata["outputs"].Dimensions.Select(d => d < 0 ? 1L : d).ToArray();
        _logits = new float[logitShape.Aggregate(1L, (a, b) => a * b)];
        _frame = new float[_width];

        OrtValue Bind<T>(T[] data, long[] shape) where T : unmanaged
        {
            var value = OrtValue.CreateTensorValueFromMemory(data, shape);
            _owned.Add(value);
            return value;
        }

        var frame = Bind(_frame, [1, _width, 1]);
        var int64 = inputs["targets"].ElementDataType == TensorElementType.Int64;
        var target = int64 ? Bind(_target64, [1, 1]) : Bind(_target32, [1, 1]);
        var length = int64 ? Bind(new long[] { 1 }, [1]) : Bind(new[] { 1 }, [1]);
        var a1 = Bind(_states[0], stateShapes[0]);
        var a2 = Bind(_states[1], stateShapes[1]);
        var b1 = Bind(_states[2], stateShapes[0]);
        var b2 = Bind(_states[3], stateShapes[1]);
        var logits = Bind(_logits, logitShape);
        _inputs = [frame, target, length, a1, a2];
        _outputs = [logits, b1, b2];
        _swappedInputs = [frame, target, length, b1, b2];
        _swappedOutputs = [logits, a1, a2];
    }

    protected override List<int>? Decode(
        IReadOnlyCollection<DisposableNamedOnnxValue> encoderOut, long featureFrames, CancellationToken cancel)
    {
        var encoded = Output(encoderOut, "outputs");
        var frames = (int)Math.Min(Output<long>(encoderOut, "encoded_lengths")[0], encoded.Dimensions[2]);
        var stride = encoded.Dimensions[2];
        if (encoded.Dimensions[1] != _width)
            throw new InvalidDataException("The encoder and the joint disagree on the frame width.");

        // [1, D, F] read frame-major, so a step copies one contiguous row.
        var source = encoded.Buffer.Span;
        var byFrame = new float[frames * _width];
        for (var d = 0; d < _width; d++)
            for (var t = 0; t < frames; t++)
                byFrame[t * _width + d] = source[d * stride + t];

        foreach (var state in _states)
            Array.Clear(state);
        var swapped = false;
        var cancelled = false;

        (int, int) Joint(int t, int last)
        {
            // A jump past the last frame ends the decode.
            if (cancel.IsCancellationRequested)
            {
                cancelled = true;
                return (Blank, frames);
            }
            byFrame.AsSpan(t * _width, _width).CopyTo(_frame);
            _target64[0] = last;
            _target32[0] = last;
            _joint.Run(_run, Inputs, swapped ? _swappedInputs : _inputs, Outputs, swapped ? _swappedOutputs : _outputs);
            var logits = _logits.AsSpan();
            return (ArgMax(logits[..Vocab.Length]), ArgMax(logits[Vocab.Length..]));
        }

        // A kept piece moves the decoder on: the state just written becomes the one read.
        var tokens = DecodeTdt(frames, Blank, Joint, () => swapped = !swapped);
        return cancelled ? null : tokens;
    }

    public override void Dispose()
    {
        foreach (var owned in _owned)
            owned.Dispose();
        _run.Dispose();
        _joint.Dispose();
        base.Dispose();
    }
}
