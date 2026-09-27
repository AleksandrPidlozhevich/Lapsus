using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Lapsus.Startup;

[SupportedOSPlatform("windows")]
// Store/MSIX autostart via startup task; HKCU Run writes land in the package private hive.
internal sealed class WindowsStoreStartupRegistration : IStartupRegistration
{
    private const string TaskId = "LapsusStartup";

    private const string StartupTaskClass = "Windows.ApplicationModel.StartupTask";
    private static readonly Guid IidStartupTaskStatics = new("ee5b60bd-a148-41a7-b26e-e8b88a1e62f8");
    private static readonly Guid IidAsyncInfo = new("00000036-0000-0000-c000-000000000046");

    private const int StaticsGetAsync = 7;
    private const int TaskRequestEnableAsync = 6;
    private const int TaskDisable = 7;
    private const int TaskGetState = 8;
    private const int AsyncInfoGetStatus = 7;
    private const int AsyncInfoGetErrorCode = 8;
    private const int AsyncOperationGetResults = 8;

    private const int AsyncStatusStarted = 0;
    private const int AsyncStatusCompleted = 1;

    private static readonly TimeSpan AsyncTimeout = TimeSpan.FromSeconds(5);

    private enum StartupTaskState
    {
        Disabled = 0,
        DisabledByUser = 1,
        Enabled = 2,
        DisabledByPolicy = 3,
        EnabledByPolicy = 4
    }

    public bool IsSupported => true;

    public bool IsEnabled => WithTask(GetState) is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    public void SetEnabled(bool enabled)
    {
        WithTask(task =>
        {
            if (enabled)
                RequestEnable(task);
            else
                Check(Slot<TaskDisableFn>(task, TaskDisable)(task));
            return 0;
        });
    }

    private static T WithTask<T>(Func<IntPtr, T> use)
    {
        var task = GetTask();
        try
        {
            return use(task);
        }
        finally
        {
            Marshal.Release(task);
        }
    }

    private static IntPtr GetTask()
    {
        var statics = GetActivationFactory(StartupTaskClass, IidStartupTaskStatics);
        try
        {
            var id = CreateHString(TaskId);
            try
            {
                Check(Slot<StaticsGetAsyncFn>(statics, StaticsGetAsync)(statics, id, out var operation));
                return AwaitPointer(operation);
            }
            finally
            {
                WindowsDeleteString(id);
            }
        }
        finally
        {
            Marshal.Release(statics);
        }
    }

    private static StartupTaskState GetState(IntPtr task)
    {
        Check(Slot<TaskGetStateFn>(task, TaskGetState)(task, out var state));
        return (StartupTaskState)state;
    }

    private static void RequestEnable(IntPtr task)
    {
        Check(Slot<TaskRequestEnableAsyncFn>(task, TaskRequestEnableAsync)(task, out var operation));
        try
        {
            Wait(operation);

            Check(Slot<GetResultsIntFn>(operation, AsyncOperationGetResults)(operation, out _));
        }
        finally
        {
            Marshal.Release(operation);
        }
    }

    private static IntPtr AwaitPointer(IntPtr operation)
    {
        try
        {
            Wait(operation);
            Check(Slot<GetResultsPtrFn>(operation, AsyncOperationGetResults)(operation, out var result));
            return result;
        }
        finally
        {
            Marshal.Release(operation);
        }
    }

    private static void Wait(IntPtr operation)
    {
        var iid = IidAsyncInfo;
        Check(Marshal.QueryInterface(operation, in iid, out var info));
        try
        {
            var getStatus = Slot<AsyncInfoGetIntFn>(info, AsyncInfoGetStatus);
            var clock = Stopwatch.StartNew();
            int status;
            while (true)
            {
                Check(getStatus(info, out status));
                if (status != AsyncStatusStarted)
                    break;
                if (clock.Elapsed > AsyncTimeout)
                    throw new TimeoutException("StartupTask did not answer.");
                Thread.Sleep(10);
            }

            if (status == AsyncStatusCompleted)
                return;

            Check(Slot<AsyncInfoGetIntFn>(info, AsyncInfoGetErrorCode)(info, out var error));
            Check(error);
            throw new OperationCanceledException("StartupTask request was cancelled.");
        }
        finally
        {
            Marshal.Release(info);
        }
    }

    private static IntPtr GetActivationFactory(string className, Guid iid)
    {
        var name = CreateHString(className);
        try
        {
            Check(RoGetActivationFactory(name, ref iid, out var factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(name);
        }
    }

    private static IntPtr CreateHString(string value)
    {
        Check(WindowsCreateString(value, (uint)value.Length, out var hstring));
        return hstring;
    }

    private static void Check(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    private static T Slot<T>(IntPtr instance, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int StaticsGetAsyncFn(IntPtr self, IntPtr taskId, out IntPtr operation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TaskRequestEnableAsyncFn(IntPtr self, out IntPtr operation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TaskDisableFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TaskGetStateFn(IntPtr self, out int state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AsyncInfoGetIntFn(IntPtr self, out int value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetResultsPtrFn(IntPtr self, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetResultsIntFn(IntPtr self, out int result);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int WindowsCreateString(string sourceString, uint length, out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);
}
