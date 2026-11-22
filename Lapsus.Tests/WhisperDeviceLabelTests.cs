using Lapsus.Speech;

namespace Lapsus.Tests;

public class WhisperDeviceLabelTests
{
    private static string Label(params string[] log)
    {
        var (onGpu, device) = WhisperRecognizer.UsedGpu(log.ToList());
        return WhisperRecognizer.DescribeDevice(onGpu, device);
    }

    [Fact]
    public void Metal_names_the_chip_not_the_runtime_package()
    {
        Assert.Equal("GPU (Apple M2 Pro)", Label(
            "ggml_metal_device_init: GPU name:   MTL0 (Apple M2 Pro)",
            "whisper_model_load:     MTL0 total size =  1623.92 MB"));
    }

    [Fact]
    public void An_older_metal_log_that_names_only_the_chip_is_read_too()
    {
        Assert.Equal("GPU (Apple M1)", Label(
            "ggml_metal_init: GPU name:   Apple M1 (MTLGPUFamilyApple7)",
            "whisper_model_load:     MTL0 total size =  1623.92 MB"));
    }

    [Fact]
    public void Vulkan_names_the_card()
    {
        Assert.Equal("GPU (NVIDIA GeForce RTX 3080)", Label(
            "ggml_vulkan: 0 = NVIDIA GeForce RTX 3080 (NVIDIA) | uma: 0 | fp16: 1",
            "whisper_model_load:  Vulkan0 total size =   873.00 MB"));
    }

    [Fact]
    public void Without_a_card_name_the_backend_stands_in()
    {
        Assert.Equal("GPU (MTL0)", Label("whisper_model_load:     MTL0 total size =  1623.92 MB"));
    }

    [Fact]
    public void Weights_on_the_cpu_read_as_cpu_even_when_a_gpu_was_found()
    {
        Assert.Equal("CPU", Label(
            "ggml_metal_device_init: GPU name:   MTL0 (Apple M2 Pro)",
            "whisper_model_load:      CPU total size =  1623.92 MB"));
    }
}
