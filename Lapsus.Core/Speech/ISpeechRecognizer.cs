namespace Lapsus.Core.Speech;

// What one dictation asks of the recognizer. Language is the one to decode in; DetectAmong, when it holds more
// than one language, lets the recognizer pick among them first (and only among them).
public sealed record SpeechRequest(
    string Language,
    string? Prompt = null,
    IReadOnlyList<string>? DetectAmong = null);

// SpeechSeconds is the speech left after silence was cut away; zero means nothing was said and Text is empty.
public sealed record SpeechResult(
    string Text,
    string Language,
    double SpeechSeconds,
    float LanguageProbability = 1f,
    float MinTokenProbability = 1f)
{
    public static SpeechResult Silence(string language) => new("", language, 0);
}

public interface ISpeechRecognizer : IDisposable
{
    bool IsLoaded { get; }

    // pcm: 16 kHz mono samples in [-1, 1].
    Task<SpeechResult> TranscribeAsync(
        ReadOnlyMemory<float> pcm, SpeechRequest request, CancellationToken cancellationToken = default);
}
