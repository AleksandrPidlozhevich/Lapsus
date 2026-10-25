using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;

namespace Lapsus.Audio;

// A Windows recording device through WinMM (the default one unless the user picked another). Asking the wave mapper for 16 kHz mono 16-bit makes
// Windows do the resampling, so Whisper gets its format with no converter of ours.
[SupportedOSPlatform("windows")]
internal sealed class WindowsAudioCapture : IAudioCapture
{
    private const int SampleRate = 16000;

    // Five minutes is the dictation's own limit; this only keeps a stuck recording from eating memory.
    private const int MaxSamples = SampleRate * 60 * 6;

    // -1 is WAVE_MAPPER: the device Windows uses by default, following the user's choice in Sound settings.
    private const int DefaultDevice = -1;

    private readonly object _gate = new();
    private readonly List<float> _samples = new(SampleRate * 30);
    private readonly Func<string?> _device;
    private WaveInEvent? _input;
    private ManualResetEventSlim? _stopped;

    public WindowsAudioCapture(Func<string?> device)
    {
        _device = device;
    }

    // Recording devices by WinMM name (cut to 31 characters by WinMM itself), in device order.
    public static IReadOnlyList<string> Microphones()
    {
        var names = new List<string>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            try
            {
                names.Add(WaveInEvent.GetCapabilities(i).ProductName);
            }
            catch (NAudio.MmException)
            {
            }
        }
        return names;
    }

    // A device chosen by name that is gone (unplugged headset) falls back to the default one.
    private static int DeviceNumber(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return DefaultDevice;
        var index = Microphones().ToList().IndexOf(name);
        return index >= 0 ? index : DefaultDevice;
    }

    public bool IsRecording
    {
        get
        {
            lock (_gate)
                return _input is not null;
        }
    }

    public event Action<float>? Level;

    public void Start()
    {
        lock (_gate)
        {
            if (_input is not null)
                return;

            _samples.Clear();
            var input = new WaveInEvent
            {
                DeviceNumber = DeviceNumber(_device()),
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 50,
                NumberOfBuffers = 4
            };
            var stopped = new ManualResetEventSlim(false);
            input.DataAvailable += OnData;
            input.RecordingStopped += (_, _) => stopped.Set();

            try
            {
                input.StartRecording();
            }
            catch (Exception ex)
            {
                input.Dispose();
                stopped.Dispose();
                throw new AudioCaptureException(ex.Message, ex);
            }

            _input = input;
            _stopped = stopped;
        }
    }

    public float[] Stop()
    {
        WaveInEvent? input;
        ManualResetEventSlim? stopped;
        lock (_gate)
        {
            input = _input;
            stopped = _stopped;
            _input = null;
            _stopped = null;
        }

        if (input is not null)
        {
            try
            {
                input.StopRecording();
                // The last buffers arrive before RecordingStopped; wait for them so the end of a phrase is kept.
                stopped?.Wait(TimeSpan.FromMilliseconds(500));
            }
            finally
            {
                input.DataAvailable -= OnData;
                input.Dispose();
                stopped?.Dispose();
            }
        }

        lock (_gate)
        {
            var result = _samples.ToArray();
            _samples.Clear();
            return result;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        double sum = 0;
        var count = e.BytesRecorded / 2;
        lock (_gate)
        {
            for (var i = 0; i < count; i++)
            {
                var sample = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                sum += sample * sample;
                if (_samples.Count < MaxSamples)
                    _samples.Add(sample);
            }
        }

        if (count > 0)
            Level?.Invoke((float)Math.Sqrt(sum / count));
    }

    public void Dispose()
    {
        Stop();
    }
}
