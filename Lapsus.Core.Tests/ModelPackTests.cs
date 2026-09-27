using Lapsus.Core.Models;

namespace Lapsus.Core.Tests;

public class ModelPackTests
{
    private const string DmlConfig = """
                                     {"model":{"decoder":{"session_options":{"provider_options":[{"dml":{}}]}}}}
                                     """;

    private const string CpuConfig = """
                                     {"model":{"decoder":{"session_options":{"provider_options":[]}}}}
                                     """;

    [Fact]
    public void Reads_the_declared_provider()
    {
        Assert.Equal([ExecutionProviders.DirectMl], GenAiPack.ReadProviders(DmlConfig));
        Assert.Empty(GenAiPack.ReadProviders(CpuConfig));
    }

    [Fact]
    public void Unreadable_config_declares_nothing()
    {
        Assert.Empty(GenAiPack.ReadProviders("not json at all"));
        Assert.Empty(GenAiPack.ReadProviders("{}"));
    }

    [Theory]
    [InlineData("cpu_and_mobile/cpu-int4-rtn-block-32", ComputeDevice.Cpu)]
    [InlineData("directml/dml-int4-awq-block-128", ComputeDevice.Gpu)]
    [InlineData("gpu/gpu-int4-awq-block-128", ComputeDevice.Gpu)]
    [InlineData("onnxruntime/webgpu/webgpu-int4-kld-block-32", ComputeDevice.Gpu)]
    [InlineData("qnn/qnn-int4", ComputeDevice.Npu)]
    [InlineData("", ComputeDevice.Cpu)]
    public void Folder_name_gives_the_device_away(string subPath, ComputeDevice expected)
    {
        Assert.Equal(expected, GenAiPack.InferDevice(subPath));
    }

    [Theory]
    [InlineData("onnxruntime/webgpu/webgpu-int4-kld-block-32", ExecutionProviders.WebGpu)]
    [InlineData("directml/dml-int4-awq-block-128", ExecutionProviders.DirectMl)]
    [InlineData("cuda/cuda-int4-rtn-block-32", ExecutionProviders.Cuda)]
    [InlineData("gpu/gpu-int4-awq-block-128", "")]
    [InlineData("cpu_and_mobile/cpu-int4", "")]
    public void Folder_name_names_the_provider_when_it_can(string subPath, string expected)
    {
        Assert.Equal(expected, GenAiPack.InferProvider(subPath));
    }

    [Fact]
    public void A_found_webgpu_pack_is_not_offered_to_a_directml_machine()
    {
        var listing = new[]
        {
            new RepoFile("onnxruntime/cpu_and_mobile/cpu-int4/genai_config.json", 1_500),
            new RepoFile("onnxruntime/cpu_and_mobile/cpu-int4/model.onnx", 500L * 1024 * 1024),
            new RepoFile("onnxruntime/webgpu/webgpu-int4/genai_config.json", 1_500),
            new RepoFile("onnxruntime/webgpu/webgpu-int4/model.onnx", 520L * 1024 * 1024)
        };
        var repo = new ModelDescriptor("id", "name", "org/repo", ModelPackScanner.Scan(listing), "");

        Assert.Equal(
            "onnxruntime/cpu_and_mobile/cpu-int4",
            repo.PickVariant(
                ComputeDevicePreference.Auto, new RuntimeCapabilities([ExecutionProviders.DirectMl]))!.SubPath);
        Assert.Equal(
            "onnxruntime/webgpu/webgpu-int4",
            repo.PickVariant(
                ComputeDevicePreference.Auto, new RuntimeCapabilities([ExecutionProviders.WebGpu]))!.SubPath);
    }

    [Fact]
    public void Declared_provider_outranks_the_folder_name()
    {
        Assert.Equal(
            ComputeDevice.Gpu,
            GenAiPack.InferDevice("cpu_and_mobile/whatever", [ExecutionProviders.DirectMl]));
    }

    [Fact]
    public void Reasoning_template_is_recognised()
    {
        Assert.True(GenAiPack.UsesThinkingTemplate(
            "{%- if enable_thinking is defined and enable_thinking is false %}"));
        Assert.False(GenAiPack.UsesThinkingTemplate("<|im_start|>assistant"));
    }

    [Fact]
    public void Scanner_finds_every_pack_with_its_size_and_device()
    {
        var listing = new[]
        {
            new RepoFile("README.md", 2_000),
            new RepoFile("onnxruntime/cpu_and_mobile/cpu-int4/genai_config.json", 1_500),
            new RepoFile("onnxruntime/cpu_and_mobile/cpu-int4/model.onnx", 100L * 1024 * 1024),
            new RepoFile("onnxruntime/webgpu/webgpu-int4/genai_config.json", 1_500),
            new RepoFile("onnxruntime/webgpu/webgpu-int4/model.onnx", 200L * 1024 * 1024)
        };

        var packs = ModelPackScanner.Scan(listing);

        Assert.Equal(2, packs.Count);
        Assert.Equal("onnxruntime/cpu_and_mobile/cpu-int4", packs[0].SubPath);
        Assert.Equal(ComputeDevice.Cpu, packs[0].Device);
        Assert.Equal(100, packs[0].SizeMb);
        Assert.Equal(ComputeDevice.Gpu, packs[1].Device);
        Assert.Equal(200, packs[1].SizeMb);
    }

    [Fact]
    public void A_transformers_js_export_holds_no_pack()
    {
        var listing = new[]
        {
            new RepoFile("onnx/model.onnx", 500L * 1024 * 1024),
            new RepoFile("onnx/model_q4.onnx", 200L * 1024 * 1024),
            new RepoFile("tokenizer.json", 10_000)
        };

        Assert.Empty(ModelPackScanner.Scan(listing));
    }

    [Fact]
    public void A_pack_at_the_repository_root_keeps_the_weights_in_its_subfolder()
    {
        var listing = new[]
        {
            new RepoFile("README.md", 2_000),
            new RepoFile("genai_config.json", 1_500),
            new RepoFile("onnx/model.onnx", 20L * 1024 * 1024),
            new RepoFile("onnx/model.onnx_data", 200L * 1024 * 1024)
        };

        var pack = Assert.Single(ModelPackScanner.Scan(listing));

        Assert.Equal("", pack.SubPath);
        Assert.Equal(220, pack.SizeMb);
        Assert.Equal(4, ModelPackScanner.FilesOf(listing, "").Count);
    }

    [Fact]
    public void One_pack_does_not_drag_in_another()
    {
        var listing = new[]
        {
            new RepoFile("cpu/genai_config.json", 1_500),
            new RepoFile("cpu/model.onnx", 10L * 1024 * 1024),
            new RepoFile("cpu/extra/weights.bin", 5L * 1024 * 1024),
            new RepoFile("cpu/gpu-int4/genai_config.json", 1_500),
            new RepoFile("cpu/gpu-int4/model.onnx", 90L * 1024 * 1024)
        };

        var cpu = ModelPackScanner.FilesOf(listing, "cpu");

        Assert.Equal(3, cpu.Count);
        Assert.Equal(15, ModelPackScanner.Scan(listing).First(p => p.SubPath == "cpu").SizeMb);
    }

    private static ModelDescriptor Repo()
    {
        return new ModelDescriptor(
            "id", "name", "org/repo",
            [
                new ModelVariant("cpu_and_mobile/cpu-int4", ComputeDevice.Cpu, "", 800),
                new ModelVariant("directml/dml-int4", ComputeDevice.Gpu, ExecutionProviders.DirectMl, 500),
                new ModelVariant("webgpu/webgpu-int4", ComputeDevice.Gpu, ExecutionProviders.WebGpu, 520)
            ],
            "license");
    }

    [Fact]
    public void Each_machine_takes_the_pack_it_can_run()
    {
        var windows = new RuntimeCapabilities([ExecutionProviders.DirectMl]);
        var mac = new RuntimeCapabilities([ExecutionProviders.WebGpu]);

        Assert.Equal(
            "directml/dml-int4",
            Repo().PickVariant(ComputeDevicePreference.Auto, windows)!.SubPath);
        Assert.Equal(
            "webgpu/webgpu-int4",
            Repo().PickVariant(ComputeDevicePreference.Auto, mac)!.SubPath);
        Assert.Equal(
            "cpu_and_mobile/cpu-int4",
            Repo().PickVariant(ComputeDevicePreference.Auto, RuntimeCapabilities.CpuOnly)!.SubPath);
    }

    [Fact]
    public void Choosing_the_processor_downloads_the_cpu_pack()
    {
        var windows = new RuntimeCapabilities([ExecutionProviders.DirectMl]);

        Assert.Equal(
            "cpu_and_mobile/cpu-int4",
            Repo().PickVariant(ComputeDevicePreference.Cpu, windows)!.SubPath);
    }

    [Fact]
    public void An_undeclared_gpu_pack_is_a_directml_or_cuda_pack()
    {
        var repo = new ModelDescriptor(
            "id", "name", "org/repo",
            [
                new ModelVariant("cpu_and_mobile/cpu-int4", ComputeDevice.Cpu, "", 800),
                new ModelVariant("gpu/gpu-int4", ComputeDevice.Gpu, "", 700)
            ],
            "license");

        Assert.Equal(
            "gpu/gpu-int4",
            repo.PickVariant(
                ComputeDevicePreference.Auto, new RuntimeCapabilities([ExecutionProviders.DirectMl]))!.SubPath);
        Assert.Equal(
            "cpu_and_mobile/cpu-int4",
            repo.PickVariant(
                ComputeDevicePreference.Auto, new RuntimeCapabilities([ExecutionProviders.WebGpu]))!.SubPath);
    }

    [Fact]
    public void A_repo_with_only_a_gpu_pack_still_installs_on_a_cpu_machine()
    {
        var gpuOnly = new ModelDescriptor(
            "id", "name", "org/repo",
            [new ModelVariant("webgpu/webgpu-int4", ComputeDevice.Gpu, ExecutionProviders.WebGpu, 520)],
            "license");

        Assert.Equal(
            "webgpu/webgpu-int4",
            gpuOnly.PickVariant(ComputeDevicePreference.Auto, RuntimeCapabilities.CpuOnly)!.SubPath);
    }
}
