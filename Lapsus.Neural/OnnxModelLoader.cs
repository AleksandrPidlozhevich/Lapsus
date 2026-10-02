using System;
using System.Collections.Generic;
using System.IO;
using Lapsus.Core.Models;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Lapsus.Neural;

public sealed record LoadedSession(
    Model Model,
    Tokenizer Tokenizer,
    ExecutionAttempt Attempt,
    bool ClosesThinking);

public static class OnnxModelLoader
{
    public static LoadedSession Load(string modelDirectory, IReadOnlyList<ExecutionAttempt> plan)
    {
        Exception? lastError = null;
        var closesThinking = UsesThinkingTemplate(modelDirectory);

        foreach (var attempt in plan)
        {
            Model? model = null;
            Tokenizer? tokenizer = null;
            try
            {
                model = CreateModel(modelDirectory, attempt.Provider);
                tokenizer = new Tokenizer(model);
                return new LoadedSession(model, tokenizer, attempt, closesThinking);
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

    private static bool UsesThinkingTemplate(string modelDirectory)
    {
        foreach (var name in new[] { "chat_template.jinja", "tokenizer_config.json" })
            try
            {
                var path = Path.Combine(modelDirectory, name);
                if (File.Exists(path) && GenAiPack.UsesThinkingTemplate(File.ReadAllText(path)))
                    return true;
            }
            catch (IOException)
            {

            }

        return false;
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
