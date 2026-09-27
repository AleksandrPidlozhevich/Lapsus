using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal static class MacClipboard
{

    private const uint Utf8 = 0x08000100;

    private const string ClipboardName = "com.apple.pasteboard.clipboard";
    private const string PlainTextFlavor = "public.utf8-plain-text";

    private const int MaxSnapshotBytes = 4 * 1024 * 1024;

    private static IntPtr _pasteboard;

    internal sealed class Snapshot
    {
        public List<Item> Items { get; } = [];

        public sealed class Item
        {
            public required IntPtr Id { get; init; }
            public List<(string Flavor, byte[] Data)> Flavors { get; } = [];
        }
    }

    public static bool WasModified()
    {
        return TryGetPasteboard(out var pasteboard) &&
               (MacOSNativeMethods.PasteboardSynchronize(pasteboard) & MacOSNativeMethods.PasteboardModified) != 0;
    }

    public static Snapshot? CaptureSnapshot()
    {
        if (!TryGetPasteboard(out var pasteboard))
            return null;

        MacOSNativeMethods.PasteboardSynchronize(pasteboard);
        if (MacOSNativeMethods.PasteboardGetItemCount(pasteboard, out var count) != 0 || count == 0)
            return null;

        var snapshot = new Snapshot();
        var total = 0;

        for (uint i = 1; i <= count; i++)
        {
            if (MacOSNativeMethods.PasteboardGetItemIdentifier(pasteboard, i, out var item) != 0)
                continue;

            if (MacOSNativeMethods.PasteboardCopyItemFlavors(pasteboard, item, out var flavors) != 0
                || flavors == IntPtr.Zero)
                continue;

            try
            {
                var entry = new Snapshot.Item { Id = item };
                var flavorCount = MacOSNativeMethods.CFArrayGetCount(flavors);
                for (long f = 0; f < flavorCount; f++)
                {
                    var flavorRef = MacOSNativeMethods.CFArrayGetValueAtIndex(flavors, f);
                    var flavor = MacOSNativeMethods.ReadCfString(flavorRef);
                    if (string.IsNullOrEmpty(flavor))
                        continue;

                    if (MacOSNativeMethods.PasteboardCopyItemFlavorData(
                            pasteboard, item, flavorRef, out var data) != 0
                        || data == IntPtr.Zero)
                        continue;

                    try
                    {
                        var length = (int)MacOSNativeMethods.CFDataGetLength(data);
                        if (length <= 0)
                            continue;

                        total += length;
                        if (total > MaxSnapshotBytes)
                            return null;

                        var bytes = new byte[length];
                        Marshal.Copy(MacOSNativeMethods.CFDataGetBytePtr(data), bytes, 0, length);
                        entry.Flavors.Add((flavor, bytes));
                    }
                    finally
                    {
                        MacOSNativeMethods.CFRelease(data);
                    }
                }

                if (entry.Flavors.Count > 0)
                    snapshot.Items.Add(entry);
            }
            finally
            {
                MacOSNativeMethods.CFRelease(flavors);
            }
        }

        return snapshot.Items.Count > 0 ? snapshot : null;
    }

    public static bool RestoreSnapshot(Snapshot? snapshot)
    {
        if (snapshot is null)
            return Clear();

        if (!TryGetPasteboard(out var pasteboard))
            return false;

        if (MacOSNativeMethods.PasteboardClear(pasteboard) != 0)
            return false;

        MacOSNativeMethods.PasteboardSynchronize(pasteboard);

        foreach (var item in snapshot.Items)
        {
            foreach (var (flavor, bytes) in item.Flavors)
            {
                var flavorRef = CreateString(flavor);
                if (flavorRef == IntPtr.Zero)
                    return false;

                var data = MacOSNativeMethods.CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
                if (data == IntPtr.Zero)
                {
                    MacOSNativeMethods.CFRelease(flavorRef);
                    return false;
                }

                try
                {
                    if (MacOSNativeMethods.PasteboardPutItemFlavor(
                            pasteboard, item.Id, flavorRef, data, 0) != 0)
                        return false;
                }
                finally
                {
                    MacOSNativeMethods.CFRelease(data);
                    MacOSNativeMethods.CFRelease(flavorRef);
                }
            }
        }

        return true;
    }

    public static string? GetText()
    {
        if (!TryGetPasteboard(out var pasteboard))
            return null;

        MacOSNativeMethods.PasteboardSynchronize(pasteboard);
        if (MacOSNativeMethods.PasteboardGetItemCount(pasteboard, out var count) != 0 || count == 0)
            return null;

        var flavor = CreateString(PlainTextFlavor);
        if (flavor == IntPtr.Zero)
            return null;

        try
        {

            if (MacOSNativeMethods.PasteboardGetItemIdentifier(pasteboard, 1, out var item) != 0)
                return null;

            if (MacOSNativeMethods.PasteboardCopyItemFlavorData(pasteboard, item, flavor, out var data) != 0
                || data == IntPtr.Zero)
                return null;

            try
            {
                var length = (int)MacOSNativeMethods.CFDataGetLength(data);
                if (length <= 0)
                    return null;

                var bytes = new byte[length];
                Marshal.Copy(MacOSNativeMethods.CFDataGetBytePtr(data), bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                MacOSNativeMethods.CFRelease(data);
            }
        }
        finally
        {
            MacOSNativeMethods.CFRelease(flavor);
        }
    }

    public static bool SetText(string text)
    {
        if (!TryGetPasteboard(out var pasteboard))
            return false;

        if (MacOSNativeMethods.PasteboardClear(pasteboard) != 0)
            return false;

        MacOSNativeMethods.PasteboardSynchronize(pasteboard);

        var flavor = CreateString(PlainTextFlavor);
        if (flavor == IntPtr.Zero)
            return false;

        var bytes = Encoding.UTF8.GetBytes(text);
        var data = MacOSNativeMethods.CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
        if (data == IntPtr.Zero)
        {
            MacOSNativeMethods.CFRelease(flavor);
            return false;
        }

        try
        {
            return MacOSNativeMethods.PasteboardPutItemFlavor(
                pasteboard, (IntPtr)1, flavor, data, 0) == 0;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(data);
            MacOSNativeMethods.CFRelease(flavor);
        }
    }

    public static bool Clear()
    {
        if (!TryGetPasteboard(out var pasteboard))
            return false;

        return MacOSNativeMethods.PasteboardClear(pasteboard) == 0;
    }

    private static bool TryGetPasteboard(out IntPtr pasteboard)
    {
        if (_pasteboard != IntPtr.Zero)
        {
            pasteboard = _pasteboard;
            return true;
        }

        var name = CreateString(ClipboardName);
        if (name == IntPtr.Zero)
        {
            pasteboard = IntPtr.Zero;
            return false;
        }

        try
        {
            if (MacOSNativeMethods.PasteboardCreate(name, out _pasteboard) != 0)
                _pasteboard = IntPtr.Zero;
        }
        finally
        {
            MacOSNativeMethods.CFRelease(name);
        }

        pasteboard = _pasteboard;
        return pasteboard != IntPtr.Zero;
    }

    private static IntPtr CreateString(string value)
    {
        return MacOSNativeMethods.CFStringCreateWithCString(IntPtr.Zero, value, Utf8);
    }
}
