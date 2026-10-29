using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Audio;

// The default macOS input device through an AudioToolbox input queue. Asking the queue for 16 kHz mono
// 16-bit PCM makes Core Audio do the resampling, as the wave mapper does on Windows. Callbacks come on the
// queue's own thread (no run loop is given).
[SupportedOSPlatform("macos")]
internal sealed class MacAudioCapture : IAudioCapture
{
    private const int SampleRate = 16000;
    private const int MaxSamples = SampleRate * 60 * 6;
    private const int BufferCount = 4;

    // 50 ms of 16-bit mono per buffer.
    private const int BufferBytes = SampleRate / 20 * 2;

    private readonly object _gate = new();
    private readonly List<float> _samples = new(SampleRate * 30);

    // Kept alive for as long as the class: the queue calls it from native code.
    private readonly AudioQueueInputCallback _callback;
    private IntPtr _queue;
    private volatile bool _running;

    public MacAudioCapture()
    {
        _callback = OnBuffer;
    }

    public bool IsRecording => _running;

    public event Action<float>? Level;

    public MicrophoneAccess Access => MacMicrophone.Access();

    public void RequestAccess() => MacMicrophone.RequestAccess();

    public void OpenAccessSettings() => MacMicrophone.OpenSettings();

    public void Start()
    {
        lock (_gate)
        {
            if (_queue != IntPtr.Zero)
                return;

            _samples.Clear();
            var format = new AudioStreamBasicDescription
            {
                SampleRate = SampleRate,
                FormatId = FormatLinearPcm,
                FormatFlags = FlagSignedInteger | FlagPacked,
                BytesPerPacket = 2,
                FramesPerPacket = 1,
                BytesPerFrame = 2,
                ChannelsPerFrame = 1,
                BitsPerChannel = 16
            };

            Check(AudioQueueNewInput(ref format, _callback, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var queue),
                "AudioQueueNewInput");
            try
            {
                for (var i = 0; i < BufferCount; i++)
                {
                    Check(AudioQueueAllocateBuffer(queue, BufferBytes, out var buffer), "AudioQueueAllocateBuffer");
                    Check(AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero), "AudioQueueEnqueueBuffer");
                }

                _running = true;
                Check(AudioQueueStart(queue, IntPtr.Zero), "AudioQueueStart");
            }
            catch
            {
                _running = false;
                AudioQueueDispose(queue, true);
                throw;
            }

            _queue = queue;
        }
    }

    public float[] Stop()
    {
        IntPtr queue;
        lock (_gate)
        {
            queue = _queue;
            _queue = IntPtr.Zero;
        }

        if (queue != IntPtr.Zero)
        {
            _running = false;
            // Immediate: at most the block in progress (50 ms) is dropped, and nothing waits on the device.
            AudioQueueStop(queue, true);
            AudioQueueDispose(queue, true);
        }

        lock (_gate)
        {
            var result = _samples.ToArray();
            _samples.Clear();
            return result;
        }
    }

    public float[] TakeRecorded()
    {
        lock (_gate)
        {
            var result = _samples.ToArray();
            _samples.Clear();
            return result;
        }
    }

    private void OnBuffer(IntPtr userData, IntPtr queue, IntPtr buffer, IntPtr startTime, uint packets, IntPtr descriptions)
    {
        try
        {
            // AudioQueueBuffer: mAudioDataBytesCapacity (UInt32, padded), mAudioData (void*), mAudioDataByteSize.
            var data = Marshal.ReadIntPtr(buffer, 8);
            var bytes = Marshal.ReadInt32(buffer, 16);
            var count = bytes / 2;
            double sum = 0;
            lock (_gate)
            {
                for (var i = 0; i < count; i++)
                {
                    var sample = Marshal.ReadInt16(data, i * 2) / 32768f;
                    sum += sample * sample;
                    if (_samples.Count < MaxSamples)
                        _samples.Add(sample);
                }
            }

            if (count > 0)
                Level?.Invoke((float)Math.Sqrt(sum / count));
        }
        finally
        {
            if (_running)
                AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
        }
    }

    private static void Check(int status, string call)
    {
        if (status != 0)
            throw new AudioCaptureException($"{call} failed (OSStatus {status}).");
    }

    public void Dispose()
    {
        Stop();
    }

    private const uint FormatLinearPcm = 0x6C70636D; // 'lpcm'
    private const uint FlagSignedInteger = 1 << 2;
    private const uint FlagPacked = 1 << 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AudioQueueInputCallback(
        IntPtr userData, IntPtr queue, IntPtr buffer, IntPtr startTime, uint packets, IntPtr descriptions);

    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueNewInput(
        ref AudioStreamBasicDescription format, AudioQueueInputCallback callback, IntPtr userData,
        IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr queue);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueAllocateBuffer(IntPtr queue, uint bytes, out IntPtr buffer);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueEnqueueBuffer(IntPtr queue, IntPtr buffer, uint packets, IntPtr descriptions);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueStop(IntPtr queue, [MarshalAs(UnmanagedType.U1)] bool immediate);

    [DllImport(AudioToolbox)]
    private static extern int AudioQueueDispose(IntPtr queue, [MarshalAs(UnmanagedType.U1)] bool immediate);
}

// The microphone permission (TCC). Until it is granted an input queue records silence, so dictation asks
// first instead of failing on an empty recording.
[SupportedOSPlatform("macos")]
internal static class MacMicrophone
{
    private const string AvFoundation = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";

    // AVAuthorizationStatus.
    private const long NotDetermined = 0;
    private const long Authorized = 3;

    private static bool _loaded;

    public static MicrophoneAccess Access()
    {
        var status = Status();
        return status switch
        {
            Authorized => MicrophoneAccess.Granted,
            NotDetermined => MicrophoneAccess.NotAsked,
            null => MicrophoneAccess.Granted,
            _ => MicrophoneAccess.Denied
        };
    }

    // macOS shows its "Lapsus would like to access the microphone" dialog the first time an input queue
    // starts; a short-lived queue is the simplest way to raise it without an Objective-C block.
    public static void RequestAccess()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            using var probe = new MacAudioCapture();
            try
            {
                probe.Start();
                System.Threading.Thread.Sleep(200);
            }
            catch (AudioCaptureException)
            {
            }
            finally
            {
                probe.Stop();
            }
        });
    }

    public static void OpenSettings() =>
        Input.MacWorkspace.OpenUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone");

    // +[AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio]; null if AVFoundation is missing.
    private static long? Status()
    {
        if (!_loaded)
        {
            _loaded = NativeLibrary.TryLoad(AvFoundation, out _);
            if (!_loaded)
                return null;
        }

        var pool = objc_autoreleasePoolPush();
        var mediaType = CFStringCreateWithCString(IntPtr.Zero, "soun", 0x08000100);
        try
        {
            var device = objc_getClass("AVCaptureDevice");
            if (device == IntPtr.Zero)
                return null;
            return objc_msgSend_long(device, sel_registerName("authorizationStatusForMediaType:"), mediaType);
        }
        finally
        {
            CFRelease(mediaType);
            objc_autoreleasePoolPop(pool);
        }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern long objc_msgSend_long(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr value);
}
