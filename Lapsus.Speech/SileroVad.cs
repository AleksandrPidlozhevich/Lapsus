using Microsoft.ML.OnnxRuntime;

namespace Lapsus.Speech;

// Silero VAD 6.2 in onnx-asr's export (the same model whisper.cpp runs for Whisper, in ggml). It reads 32 ms hops
// with 4 ms of the previous hop in front, carrying its state, and gives each hop a speech probability. A hop is one
// run, ~30 a second, so its buffers are bound once and the state flips between two of them.
internal sealed class SileroVad : IDisposable
{
    private const int Hop = 512;
    private const int Context = 64;
    private const float Threshold = 0.5f;

    // A click or a cough can top Threshold for a hop; a syllable lasts longer.
    internal const int MinSpeechHops = 3;

    private static readonly string[] Inputs = ["input", "state", "sr"];
    private static readonly string[] Outputs = ["output", "stateN"];

    private readonly InferenceSession _session;
    private readonly RunOptions _run = new();
    private readonly float[] _frame = new float[Context + Hop];
    private readonly float[] _probability = new float[1];
    private readonly float[][] _states = [new float[2 * 128], new float[2 * 128]];
    private readonly OrtValue[] _inputs, _swappedInputs, _outputs, _swappedOutputs;
    private readonly OrtValue[] _owned;

    public SileroVad(string path, SessionOptions options)
    {
        _session = new InferenceSession(path, options);
        var frame = OrtValue.CreateTensorValueFromMemory(_frame, [1, _frame.Length]);
        var sr = OrtValue.CreateTensorValueFromMemory(new long[] { 16000 }, [1]);
        var a = OrtValue.CreateTensorValueFromMemory(_states[0], [2, 1, 128]);
        var b = OrtValue.CreateTensorValueFromMemory(_states[1], [2, 1, 128]);
        var probability = OrtValue.CreateTensorValueFromMemory(_probability, [1, 1]);
        _inputs = [frame, a, sr];
        _outputs = [probability, b];
        _swappedInputs = [frame, b, sr];
        _swappedOutputs = [probability, a];
        _owned = [frame, sr, a, b, probability];
    }

    public bool HasSpeech(float[] pcm) => Speaks(Probabilities(pcm));

    internal static bool Speaks(IEnumerable<float> probabilities)
    {
        var run = 0;
        foreach (var p in probabilities)
            if ((run = p >= Threshold ? run + 1 : 0) >= MinSpeechHops)
                return true;
        return false;
    }

    // Lazy, so Speaks stops running the model at the first syllable.
    private IEnumerable<float> Probabilities(float[] pcm)
    {
        foreach (var state in _states)
            Array.Clear(state);
        var swapped = false;
        for (var start = 0; start < pcm.Length; start += Hop)
        {
            // Hop k is samples [k·512 − 64, k·512 + 512), zeros outside the recording.
            var from = Math.Max(start - Context, 0);
            var to = Math.Min(start + Hop, pcm.Length);
            Array.Clear(_frame);
            pcm.AsSpan(from, to - from).CopyTo(_frame.AsSpan(from - (start - Context)));

            _session.Run(_run, Inputs, swapped ? _swappedInputs : _inputs, Outputs, swapped ? _swappedOutputs : _outputs);
            swapped = !swapped;
            yield return _probability[0];
        }
    }

    public void Dispose()
    {
        foreach (var value in _owned)
            value.Dispose();
        _run.Dispose();
        _session.Dispose();
    }
}
