using System;
using System.IO;

namespace Lapsus;

internal static class CrashLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus", "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(e.ExceptionObject as Exception, e.IsTerminating ? "fatal" : "non-terminating");

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception, "unobserved task");
            e.SetObserved();
        };
    }

    public static void Log(Exception ex, string context)
    {
        Write(ex, context);
    }

    private static void Write(Exception? ex, string kind)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:u}] ({kind}) {ex}\n\n");
        }
        catch
        {
            // A crash handler must never itself throw.
        }
    }
}
