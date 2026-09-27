using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using static Lapsus.Input.NativeMethods;

namespace Lapsus.Input;

[SupportedOSPlatform("windows")]
internal static class WindowsClipboard
{

    private const int OpenAttempts = 8;

    private const int OpenRetryDelayMs = 15;

    private const int MaxSnapshotBytes = 4 * 1024 * 1024;

    private const uint CfOwnerDisplay = 0x0080;

    private const uint CfDspText = 0x0081;
    private const uint CfDspBitmap = 0x0082;
    private const uint CfDspMetafilePict = 0x0083;
    private const uint CfDspEnhMetafile = 0x008E;

    internal sealed class Snapshot
    {
        public List<(uint Format, byte[] Data)> Formats { get; } = [];
    }

    public static uint SequenceNumber()
    {
        return GetClipboardSequenceNumber();
    }

    public static Snapshot? CaptureSnapshot()
    {
        if (!TryOpen())
            return null;

        try
        {
            var snapshot = new Snapshot();
            var total = 0;
            for (var format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                if (IsSyntheticFormat(format))
                    continue;

                var handle = GetClipboardData(format);
                if (handle == IntPtr.Zero)
                    continue;

                var size = (int)GlobalSize(handle);
                if (size <= 0)
                    continue;

                total += size;
                if (total > MaxSnapshotBytes)
                    return null;

                var pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                    continue;

                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(pointer, bytes, 0, size);
                    snapshot.Formats.Add((format, bytes));
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }

            return snapshot.Formats.Count > 0 ? snapshot : null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool RestoreSnapshot(Snapshot? snapshot)
    {
        if (snapshot is null)
        {
            Clear();
            return true;
        }

        if (!TryOpen())
            return false;

        try
        {
            EmptyClipboard();
            foreach (var (format, data) in snapshot.Formats)
            {
                var block = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
                if (block == IntPtr.Zero)
                    return false;

                var pointer = GlobalLock(block);
                if (pointer == IntPtr.Zero)
                {
                    GlobalFree(block);
                    return false;
                }

                try
                {
                    Marshal.Copy(data, 0, pointer, data.Length);
                }
                finally
                {
                    GlobalUnlock(block);
                }

                if (SetClipboardData(format, block) == IntPtr.Zero)
                {
                    GlobalFree(block);
                    return false;
                }
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static string? GetText()
    {
        if (!TryOpen())
            return null;

        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
                return null;

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
                return null;

            try
            {

                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool SetText(string text)
    {
        var bytes = (text.Length + 1) * sizeof(char);
        var block = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
        if (block == IntPtr.Zero)
            return false;

        var owned = false;
        try
        {
            var pointer = GlobalLock(block);
            if (pointer == IntPtr.Zero)
                return false;

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(block);
            }

            if (!TryOpen())
                return false;

            try
            {
                EmptyClipboard();
                if (SetClipboardData(CF_UNICODETEXT, block) == IntPtr.Zero)
                    return false;

                owned = true;
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            if (!owned)
                GlobalFree(block);
        }
    }

    public static void Clear()
    {
        if (!TryOpen())
            return;

        try
        {
            EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool IsSyntheticFormat(uint format)
    {
        return format is CfOwnerDisplay or CfDspText or CfDspBitmap or CfDspMetafilePict or CfDspEnhMetafile;
    }

    private static bool TryOpen()
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
                return true;

            Thread.Sleep(OpenRetryDelayMs);
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern uint EnumClipboardFormats(uint format);
}
