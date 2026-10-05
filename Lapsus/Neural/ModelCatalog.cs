using Lapsus.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lapsus.Neural;

public static class ModelCatalog
{
    private const string QwenLicense = "Qwen — Apache-2.0 (model card on Hugging Face)";
    private const string PhiLicense = "Microsoft Phi — MIT (model card on Hugging Face)";

    public static readonly IReadOnlyList<ModelDescriptor> Available =
    [
        new(

            "qwen3-0.6b-instruct-int4",
            "Qwen3 0.6B Instruct (INT4)",
            "onnx-community/Qwen3-0.6B-ONNX",
            [
                new ModelVariant("onnxruntime/cpu_and_mobile/cpu-int4-kld-block-128", ComputeDevice.Cpu, "", 511),
                new ModelVariant("onnxruntime/webgpu/webgpu-int4-kld-block-32", ComputeDevice.Gpu,
                    ExecutionProviders.WebGpu, 529),
                new ModelVariant("onnxruntime/cuda/cuda-int4-kld-block-128", ComputeDevice.Gpu,
                    ExecutionProviders.Cuda, 492)
            ],
            QwenLicense,
            700),
        new(

            "qwen25-0.5b-instruct-int4",
            "Qwen2.5 0.5B Instruct (INT4)",
            "xiaoyao9184/Qwen2.5-0.5B-Instruct-onnx-genai",
            [
                new ModelVariant("cpu_and_mobile/cpu-int4-rtn-block-32", ComputeDevice.Cpu, "", 837),
                new ModelVariant("directml/dml-int4-awq-block-128", ComputeDevice.Gpu,
                    ExecutionProviders.DirectMl, 522),
                new ModelVariant("cuda/cuda-int4-rtn-block-32", ComputeDevice.Gpu,
                    ExecutionProviders.Cuda, 545)
            ],
            QwenLicense,
            600),
        new(
            "qwen3-1.7b-instruct-int4",
            "Qwen3 1.7B Instruct (INT4)",
            "onnx-community/Qwen3-1.7B-ONNX",
            [
                new ModelVariant("onnxruntime/cpu_and_mobile/cpu-int4-kld-block-128", ComputeDevice.Cpu, "", 1354),
                new ModelVariant("onnxruntime/webgpu/webgpu-int4-kld-block-32", ComputeDevice.Gpu,
                    ExecutionProviders.WebGpu, 1421),
                new ModelVariant("onnxruntime/cuda/cuda-int4-kld-block-128", ComputeDevice.Gpu,
                    ExecutionProviders.Cuda, 1319)
            ],
            QwenLicense,
            1700),
        new(

            "phi35-mini-instruct-int4",
            "Phi-3.5 mini Instruct (INT4)",
            "microsoft/Phi-3.5-mini-instruct-onnx",
            [
                new ModelVariant("cpu_and_mobile/cpu-int4-awq-block-128-acc-level-4", ComputeDevice.Cpu, "", 2653),
                new ModelVariant("gpu/gpu-int4-awq-block-128", ComputeDevice.Gpu, "", 2214)
            ],
            PhiLicense,
            2600)
    ];

    public static ModelDescriptor? Find(string? id)
    {
        return id is null ? null : Available.FirstOrDefault(m => m.Id == id);
    }

    public const string CustomIdPrefix = "custom-";

    public static bool IsCustom(string? id)
    {
        return id is not null && id.StartsWith(CustomIdPrefix, StringComparison.Ordinal);
    }

    public static ModelDescriptor CreateCustom(
        string repo, string subPath, IReadOnlyList<ModelVariant> variants)
    {
        var raw = string.IsNullOrEmpty(subPath) ? repo : $"{repo}-{subPath}";
        var name = string.IsNullOrEmpty(subPath)
            ? repo
            : $"{repo} · {subPath.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1]}";

        return new ModelDescriptor(
            CustomIdPrefix + Sanitize(raw),
            name,
            repo,
            variants.Count > 0 ? variants : [new ModelVariant(subPath, GenAiPack.InferDevice(subPath))],
            "User-provided model — see its card on Hugging Face for the license.");
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        var lastDash = false;
        foreach (var ch in value.ToLowerInvariant())
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }

        var s = sb.ToString().Trim('-');
        if (s.Length > 80)
            s = s[..80].Trim('-');
        return s.Length == 0 ? "model" : s;
    }
}
