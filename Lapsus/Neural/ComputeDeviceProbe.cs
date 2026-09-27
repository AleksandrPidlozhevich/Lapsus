using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lapsus.Core.Models;

namespace Lapsus.Neural;

public static class ComputeDeviceProbe
{
    private static readonly object Gate = new();
    private static RuntimeCapabilities? _capabilities;

    public static RuntimeCapabilities Capabilities()
    {
        lock (Gate)
        {
            _capabilities ??= Detect();
            return _capabilities;
        }
    }

    public static string Label(string? provider)
    {
        return provider switch
        {
            null or "" => "CPU",
            ExecutionProviders.DirectMl => "GPU (DirectML)",
            ExecutionProviders.Cuda => "GPU (CUDA)",
            ExecutionProviders.WebGpu => OperatingSystem.IsMacOS() ? "GPU (Metal)" : "GPU (WebGPU)",
            ExecutionProviders.Qnn => "NPU (QNN)",
            _ => provider.ToUpperInvariant()
        };
    }

    private static RuntimeCapabilities Detect()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return WindowsHasHardwareDevice()
                    ? new RuntimeCapabilities([ExecutionProviders.DirectMl])
                    : RuntimeCapabilities.CpuOnly;

            if (OperatingSystem.IsMacOS())
                return new RuntimeCapabilities([ExecutionProviders.WebGpu]);
        }
        catch
        {

        }

        return RuntimeCapabilities.CpuOnly;
    }

    [SupportedOSPlatform("windows")]
    private static bool WindowsHasHardwareDevice()
    {
        const int D3D_DRIVER_TYPE_HARDWARE = 1;
        const uint D3D11_SDK_VERSION = 7;

        var hr = D3D11CreateDevice(
            IntPtr.Zero,
            D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            0,
            D3D11_SDK_VERSION,
            out var device,
            out _,
            out var context);

        if (context != IntPtr.Zero)
            Marshal.Release(context);
        if (device != IntPtr.Zero)
            Marshal.Release(device);

        return hr >= 0;
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        out int pFeatureLevel,
        out IntPtr ppImmediateContext);
}
