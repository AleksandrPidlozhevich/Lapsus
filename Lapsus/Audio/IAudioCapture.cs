using System;

namespace Lapsus.Audio;

// The microphone, as dictation needs it: 16 kHz mono samples in [-1, 1], from Start to Stop.
internal interface IAudioCapture : IDisposable
{
    // Throws AudioCaptureException when there is no microphone or it cannot be opened.
    void Start();

    // Everything recorded since Start; the capture can be started again afterwards.
    float[] Stop();

    bool IsRecording { get; }

    // Loudness of the latest block (RMS, 0..1), raised on a capture thread.
    event Action<float>? Level;

    // Whether the OS lets the app record. Windows answers per device at Start, so it reports Granted here.
    MicrophoneAccess Access => MicrophoneAccess.Granted;

    // Raises the OS's own permission dialog, where there is one.
    void RequestAccess()
    {
    }

    // Opens the OS's privacy settings for the microphone, where they exist.
    void OpenAccessSettings()
    {
    }
}

internal enum MicrophoneAccess
{
    Granted,
    NotAsked,
    Denied
}

internal sealed class AudioCaptureException(string message, Exception? inner = null) : Exception(message, inner);

internal static class AudioCaptureFactory
{
    // device: the microphone's name as Microphones() lists it, read at every Start; null is the system default.
    public static IAudioCapture CreateForCurrentPlatform(Func<string?>? device = null)
    {
#if WINDOWS_AUDIO
        if (OperatingSystem.IsWindows())
            return new WindowsAudioCapture(device ?? (() => null));
#endif
        // macOS records from the system's input device; picking another one is not offered there yet.
        if (OperatingSystem.IsMacOS())
            return new MacAudioCapture();
        return new NullAudioCapture();
    }

    public static System.Collections.Generic.IReadOnlyList<string> Microphones()
    {
#if WINDOWS_AUDIO
        if (OperatingSystem.IsWindows())
            return WindowsAudioCapture.Microphones();
#endif
        return [];
    }
}

internal sealed class NullAudioCapture : IAudioCapture
{
    public bool IsRecording => false;

#pragma warning disable CS0067
    public event Action<float>? Level;
#pragma warning restore CS0067

    public void Start() => throw new AudioCaptureException("No microphone support on this platform.");

    public float[] Stop() => [];

    public void Dispose()
    {
    }
}
