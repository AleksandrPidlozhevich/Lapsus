using Lapsus.Core.Models;
using Microsoft.ML.OnnxRuntimeGenAI;
using System;
using System.Collections.Generic;

namespace Lapsus.Neural;

public sealed record LoadedSession(
    Model Model,
    Tokenizer Tokenizer,
    ExecutionAttempt Attempt);

public static class OnnxModelLoader
{
    public static LoadedSession Load(string modelDirectory, IReadOnlyList<ExecutionAttempt> plan)
    {
        Exception? lastError = null;

        foreach (var attempt in plan)
        {
            Model? model = null;
            Tokenizer? tokenizer = null;
            try
            {
                model = CreateModel(modelDirectory, attempt.Provider);
                tokenizer = new Tokenizer(model);
                return new LoadedSession(model, tokenizer, attempt);
            }
            catch (Exception ex)
            {
                lastError = ex;
                SafeDispose(tokenizer);
                SafeDispose(model);
            }
        }

        throw lastError ?? new InvalidOperationException("No execution provider could load the model.");
    }

    private static Model CreateModel(string modelDirectory, string? provider)
    {
        if (provider is not null)
        {
            using var config = new Config(modelDirectory);
            config.ClearProviders();
            config.AppendProvider(provider);
            return new Model(config);
        }

        try
        {
            using var cpuConfig = new Config(modelDirectory);
            cpuConfig.ClearProviders();
            return new Model(cpuConfig);
        }
        catch
        {
            return new Model(modelDirectory);
        }
    }

    internal static void SafeDispose(IDisposable? value)
    {
        if (value is null)
            return;
        try
        {
            value.Dispose();
        }
        catch
        {

        }
    }
}
