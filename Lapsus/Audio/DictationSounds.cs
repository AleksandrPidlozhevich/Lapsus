using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Lapsus.Audio;

// Two short tones: rising when the microphone opens, falling when it closes. Made here, not shipped as files.
// UI thread only.
internal static class DictationSounds
{
    private const int Rate = 22050;

    private static readonly Lazy<byte[]> StartWav = new(() => Tones(587.33, 880));
    private static readonly Lazy<byte[]> StopWav = new(() => Tones(880, 587.33));

    public static void Play(bool start)
    {
        try
        {
            var wav = (start ? StartWav : StopWav).Value;
            if (OperatingSystem.IsWindows())
                Windows.Play(start, wav);
            else if (OperatingSystem.IsMacOS())
                Mac.Play(start, wav);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No sound is no loss.
        }
    }

    // Two 75 ms notes with a soft attack and decay, quiet enough not to startle.
    internal static byte[] Tones(double first, double second)
    {
        const double noteSeconds = 0.075;
        var perNote = (int)(Rate * noteSeconds);
        var samples = new short[perNote * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (i % perNote) / (double)Rate;
            var frequency = i < perNote ? first : second;
            var envelope = Math.Min(1, t / 0.005) * Math.Exp(-t * 28);
            samples[i] = (short)(Math.Sin(2 * Math.PI * frequency * t) * envelope * 0.22 * short.MaxValue);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var sample in samples)
            writer.Write(sample);
        writer.Flush();
        return stream.ToArray();
    }

    private static class Windows
    {
        private const uint SndAsync = 0x1;
        private const uint SndNoDefault = 0x2;
        private const uint SndMemory = 0x4;

        // PlaySound reads the buffer while it plays, so each tone lives in unmanaged memory for good.
        private static IntPtr _start;
        private static IntPtr _stop;

        public static void Play(bool start, byte[] wav)
        {
            ref var slot = ref start ? ref _start : ref _stop;
            if (slot == IntPtr.Zero)
            {
                slot = Marshal.AllocHGlobal(wav.Length);
                Marshal.Copy(wav, 0, slot, wav.Length);
            }

            PlaySound(slot, IntPtr.Zero, SndAsync | SndNoDefault | SndMemory);
        }

        [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
        private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
    }

    private static class Mac
    {
        private static IntPtr _start;
        private static IntPtr _stop;

        public static void Play(bool start, byte[] wav)
        {
            ref var sound = ref start ? ref _start : ref _stop;
            if (sound == IntPtr.Zero)
            {
                var data = Send(Class("NSData"), "dataWithBytes:length:", wav, (ulong)wav.Length);
                sound = Send(Send(Class("NSSound"), "alloc"), "initWithData:", data);
                if (sound == IntPtr.Zero)
                    return;
            }

            Send(sound, "stop");
            Send(sound, "play");
        }

        private static IntPtr Class(string name) => objc_getClass(name);

        private static IntPtr Send(IntPtr receiver, string selector) =>
            objc_msgSend(receiver, sel_registerName(selector));

        private static IntPtr Send(IntPtr receiver, string selector, IntPtr arg) =>
            objc_msgSend(receiver, sel_registerName(selector), arg);

        private static IntPtr Send(IntPtr receiver, string selector, byte[] bytes, ulong length) =>
            objc_msgSend(receiver, sel_registerName(selector), bytes, length);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, byte[] bytes, ulong length);
    }
}
