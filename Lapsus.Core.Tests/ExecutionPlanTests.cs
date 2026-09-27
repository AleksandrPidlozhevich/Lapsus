using Lapsus.Core.Models;

namespace Lapsus.Core.Tests;

public class ExecutionPlanTests
{
    private static readonly RuntimeCapabilities Windows = new([ExecutionProviders.DirectMl]);
    private static readonly RuntimeCapabilities Mac = new([ExecutionProviders.WebGpu]);

    [Fact]
    public void Cpu_choice_never_tries_an_accelerator()
    {
        var plan = ExecutionPlan.Build(
            ComputeDevicePreference.Cpu, Windows, ComputeDevice.Gpu, [ExecutionProviders.DirectMl]);

        Assert.Equal([ExecutionAttempt.Cpu], plan);
    }

    [Fact]
    public void Declared_provider_is_tried_first_then_cpu()
    {
        var plan = ExecutionPlan.Build(
            ComputeDevicePreference.Auto, Windows, ComputeDevice.Gpu, [ExecutionProviders.DirectMl]);

        Assert.Equal(ExecutionProviders.DirectMl, plan[0].Provider);
        Assert.Equal(ComputeDevice.Gpu, plan[0].Device);
        Assert.Equal(ExecutionAttempt.Cpu, plan[^1]);
    }

    [Fact]
    public void Gpu_pack_that_declares_nothing_takes_the_machine_provider()
    {
        var plan = ExecutionPlan.Build(ComputeDevicePreference.Gpu, Windows, ComputeDevice.Gpu, []);

        Assert.Equal(ExecutionProviders.DirectMl, plan[0].Provider);
    }

    [Fact]
    public void Cpu_pack_is_never_pushed_onto_the_gpu()
    {
        var plan = ExecutionPlan.Build(ComputeDevicePreference.Gpu, Windows, ComputeDevice.Cpu, []);

        Assert.Equal([ExecutionAttempt.Cpu], plan);
    }

    [Fact]
    public void Provider_the_runtime_lacks_is_skipped()
    {
        var plan = ExecutionPlan.Build(
            ComputeDevicePreference.Gpu, Windows, ComputeDevice.Gpu, [ExecutionProviders.WebGpu]);

        Assert.Equal([ExecutionAttempt.Cpu], plan);
    }

    [Fact]
    public void Webgpu_pack_runs_on_the_mac_build()
    {
        var plan = ExecutionPlan.Build(
            ComputeDevicePreference.Auto, Mac, ComputeDevice.Gpu, [ExecutionProviders.WebGpu]);

        Assert.Equal(ExecutionProviders.WebGpu, plan[0].Provider);
    }

    [Fact]
    public void Machine_with_no_accelerator_plans_cpu_only()
    {
        var plan = ExecutionPlan.Build(
            ComputeDevicePreference.Auto, RuntimeCapabilities.CpuOnly, ComputeDevice.Gpu,
            [ExecutionProviders.DirectMl]);

        Assert.Equal([ExecutionAttempt.Cpu], plan);
    }

    [Fact]
    public void Npu_choice_falls_back_to_cpu_when_nothing_drives_one()
    {
        var plan = ExecutionPlan.Build(
            ComputeDevicePreference.Npu, Windows, ComputeDevice.Gpu, [ExecutionProviders.DirectMl]);

        Assert.Equal([ExecutionAttempt.Cpu], plan);
    }

    [Fact]
    public void A_provider_is_never_attempted_twice()
    {
        var plan = ExecutionPlan.Build(
            ComputeDevicePreference.Auto, Windows, ComputeDevice.Gpu,
            [ExecutionProviders.DirectMl, "directml"]);

        Assert.Equal(2, plan.Count);
    }

    [Fact]
    public void Capabilities_list_only_devices_they_can_drive()
    {
        Assert.Equal([ComputeDevice.Cpu, ComputeDevice.Gpu], Windows.AvailableDevices());
        Assert.Equal([ComputeDevice.Cpu], RuntimeCapabilities.CpuOnly.AvailableDevices());
    }
}
