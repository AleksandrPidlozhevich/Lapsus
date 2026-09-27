using System;
using System.Diagnostics;
using System.Threading;

namespace Lapsus;

internal static class ShutdownGuard
{

    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);

    private static int _armed;

    public static void Arm()
    {
        if (Interlocked.Exchange(ref _armed, 1) != 0)
            return;

        var thread = new Thread(Watch)
        {
            IsBackground = true,
            Name = "Lapsus.ShutdownGuard"
        };
        thread.Start();
    }

    private static void Watch()
    {
        Thread.Sleep(Grace);

        var kill = new Thread(Kill) { IsBackground = true, Name = "Lapsus.ShutdownGuard.Kill" };
        kill.Start();

        try
        {
            Environment.Exit(0);
        }
        catch
        {
        }
    }

    private static void Kill()
    {
        Thread.Sleep(ExitGrace);
        try
        {
            Process.GetCurrentProcess().Kill();
        }
        catch
        {
        }
    }
}
