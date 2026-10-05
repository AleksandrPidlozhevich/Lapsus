using Avalonia.Threading;
using System;
using System.IO;
using System.Threading;

namespace Lapsus;

internal sealed class SingleInstance : IDisposable
{
    private static readonly string MutexName =
        OperatingSystem.IsWindows() ? @"Local\Lapsus.SingleInstance" : "Lapsus.SingleInstance";

    private static readonly string EventName = @"Local\Lapsus.ShowSettings";

    private static readonly string ShowSignalPath =
        Path.Combine(Path.GetTempPath(), "Lapsus.ShowSettings.signal");

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showEvent;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, EventWaitHandle? showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);

        if (!createdNew)
        {
            try
            {
                PulseShowSignal();
            }
            finally
            {
                mutex.Dispose();
            }

            return null;
        }

        ClearShowSignal();

        EventWaitHandle? showEvent = null;
        if (OperatingSystem.IsWindows())
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        return new SingleInstance(mutex, showEvent);
    }

    public void StartWatching(Action onShowRequested)
    {
        var thread = new Thread(() =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                    if (WaitForShowSignal(OperatingSystem.IsWindows() ? 500 : 2_000))
                        Dispatcher.UIThread.Post(onShowRequested);
            }
            catch (ObjectDisposedException)
            {

            }
        })
        {
            IsBackground = true,
            Name = "Lapsus.SingleInstance"
        };
        thread.Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {

        }

        _mutex.Dispose();
        _showEvent?.Dispose();
        ClearShowSignal();
        _cts.Dispose();
    }

    private static void PulseShowSignal()
    {
        if (OperatingSystem.IsWindows())
        {
            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            showEvent.Set();
            return;
        }

        File.WriteAllBytes(ShowSignalPath, []);
    }

    private bool WaitForShowSignal(int timeoutMs)
    {
        if (_showEvent is not null)
            return _showEvent.WaitOne(timeoutMs);

        if (TryConsumeShowSignal())
            return true;

        Thread.Sleep(timeoutMs);
        return TryConsumeShowSignal();
    }

    private static bool TryConsumeShowSignal()
    {
        try
        {
            if (!File.Exists(ShowSignalPath))
                return false;

            File.Delete(ShowSignalPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void ClearShowSignal()
    {
        try
        {
            if (File.Exists(ShowSignalPath))
                File.Delete(ShowSignalPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
