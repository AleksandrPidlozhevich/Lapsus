using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lapsus.Speech;

// Silero VAD 6.2 in onnx-asr's export (the same model whisper.cpp runs for Whisper, in ggml). It reads 32 ms hops
// with 4 ms of the previous hop in front, carrying its state, and gives each hop a speech probability.
internal sealed class SileroVad : IDisposable
{
    private const int Hop = 512;
    private const int Context = 64;
    private const float Threshold = 0.5f;

    // A click or a cough can top Threshold for a hop; a syllable lasts longer.
    internal const int MinSpeechHops = 3;

    private readonly InferenceSession _session;

    public SileroVad(string path, SessionOptions options) => _session = new InferenceSession(path, options);

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
        var state = new DenseTensor<float>([2, 1, 128]);
        var sr = new DenseTensor<long>(new[] { 16000L }, [1]);
        var frame = new float[Context + Hop];
        for (var start = 0; start < pcm.Length; start += Hop)
        {
            // Hop k is samples [k·512 − 64, k·512 + 512), zeros outside the recording.
            for (var i = 0; i < frame.Length; i++)
            {
                var at = start - Context + i;
                frame[i] = at >= 0 && at < pcm.Length ? pcm[at] : 0f;
            }

            using var output = _session.Run(
            [
                NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(frame, [1, frame.Length])),
                NamedOnnxValue.CreateFromTensor("state", state),
                NamedOnnxValue.CreateFromTensor("sr", sr),
            ]);
            var stateN = output.First(v => v.Name == "stateN").AsTensor<float>();
            state = new DenseTensor<float>(stateN.ToArray(), [2, 1, 128]);
            yield return output.First(v => v.Name == "output").AsTensor<float>()[0, 0];
        }
    }

    public void Dispose() => _session.Dispose();
}
