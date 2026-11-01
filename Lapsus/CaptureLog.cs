using System;
using System.IO;

namespace Lapsus;

// Never records typed text.
internal static class CaptureLog
{
    private const long MaxBytes = 512 * 1024;

    private static readonly object Gate = new();

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus", "capture.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(LogPath, LogPath + ".1", overwrite: true);

                File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:u}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never affect capture.
        }
    }
}
