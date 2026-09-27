using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Lapsus.Core.Models;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Lapsus.Neural;

internal sealed class OnnxTextGenerator : IDisposable
{

    private const int MaxNewTokens = 256;

    private const int TokensPerChar = 2;

    private const int TokenSlack = 24;

    private const int ReusableMaxLength = 512;

    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly bool _closeThinking;

    private GeneratorParams? _params;
    private Generator? _generator;

    private bool _reuseRefused;

    private int[] _cached = [];

    public OnnxTextGenerator(Model model, Tokenizer tokenizer, bool closeThinking)
    {
        _model = model;
        _tokenizer = tokenizer;
        _closeThinking = closeThinking;
    }

    public string Complete(
        string systemPrompt, string userText, int answerChars, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var budget = TokenBudget(answerChars);
        if (budget == 0)
            return string.Empty;

        var tokens = Encode(systemPrompt, userText);

        if (CanReuse(tokens, budget))
            try
            {
                var generator = Prefill(tokens, cancellationToken);
                return Decode(generator, budget, answerChars, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {

                RefuseReuse();
            }

        return CompleteOnce(tokens, budget, answerChars, cancellationToken);
    }

    public void WarmUp(
        string systemPrompt, string userText, int answerChars, CancellationToken cancellationToken)
    {
        var budget = TokenBudget(answerChars);
        if (budget == 0)
            return;

        var tokens = Encode(systemPrompt, userText);
        if (!CanReuse(tokens, budget))
            return;

        try
        {
            Prefill(tokens, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {

            RefuseReuse();
        }
    }

    public void Dispose()
    {
        DropGenerator();
    }

    private Generator Prefill(int[] tokens, CancellationToken cancellationToken)
    {
        var shared = SharedPrefix(_cached, tokens);

        if (_generator is null || shared == 0)
        {
            DropGenerator();
            _params = NewParams(ReusableMaxLength);
            _generator = new Generator(_model, _params);
            shared = 0;
        }
        else
        {

            if (shared >= tokens.Length)
                shared = tokens.Length - 1;

            _generator.RewindTo((ulong)shared);
        }

        cancellationToken.ThrowIfCancellationRequested();

        _cached = [];
        _generator.AppendTokens(tokens.AsSpan(shared));
        _cached = tokens;
        return _generator;
    }

    private string CompleteOnce(
        int[] tokens, int budget, int answerChars, CancellationToken cancellationToken)
    {
        using var generatorParams = NewParams(tokens.Length + budget);
        using var generator = new Generator(_model, generatorParams);
        generator.AppendTokens(tokens);
        return Decode(generator, budget, answerChars, cancellationToken);
    }

    private string Decode(
        Generator generator, int budget, int answerChars, CancellationToken cancellationToken)
    {
        using var stream = _tokenizer.CreateStream();
        var text = new StringBuilder(answerChars + 16);
        var runaway = answerChars * 4 + 64;

        var seen = generator.GetSequence(0).Length;

        for (var generated = 0; generated < budget; generated++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (generator.IsDone())
                return text.ToString();

            generator.GenerateNextToken();

            var sequence = generator.GetSequence(0);
            if (sequence.Length <= seen)
                return text.ToString();

            seen = sequence.Length;
            var piece = stream.Decode(sequence[^1]);
            text.Append(piece);

            if (piece.Contains('\n') || piece.Contains('\r'))
                return text.ToString();

            if (text.Length > runaway)
                return string.Empty;
        }

        return generator.IsDone() ? text.ToString() : string.Empty;
    }

    internal static int TokenBudget(int answerChars)
    {
        var wanted = answerChars * TokensPerChar + TokenSlack;
        return wanted > MaxNewTokens ? 0 : wanted;
    }

    private static int SharedPrefix(int[] cached, int[] tokens)
    {
        var limit = Math.Min(cached.Length, tokens.Length);
        var shared = 0;
        while (shared < limit && cached[shared] == tokens[shared])
            shared++;

        return shared;
    }

    private GeneratorParams NewParams(int maxLength)
    {
        var generatorParams = new GeneratorParams(_model);
        generatorParams.SetSearchOption("max_length", maxLength);
        generatorParams.SetSearchOption("min_length", 0);
        generatorParams.SetSearchOption("temperature", 0.0);
        generatorParams.SetSearchOption("top_k", 1);
        generatorParams.SetSearchOption("top_p", 1.0);
        generatorParams.SetSearchOption("do_sample", false);
        generatorParams.SetSearchOption("repetition_penalty", 1.0);
        return generatorParams;
    }

    private bool CanReuse(int[] tokens, int budget)
    {
        return !_reuseRefused && tokens.Length + budget <= ReusableMaxLength;
    }

    private void RefuseReuse()
    {
        _reuseRefused = true;
        DropGenerator();
    }

    private void DropGenerator()
    {
        _cached = [];
        OnnxModelLoader.SafeDispose(_generator);
        OnnxModelLoader.SafeDispose(_params);
        _generator = null;
        _params = null;
    }

    private int[] Encode(string systemPrompt, string userText)
    {
        using var sequences = _tokenizer.Encode(
            BuildPrompt(_tokenizer, systemPrompt, userText, _closeThinking));
        return sequences[0].ToArray();
    }

    private static string BuildPrompt(
        Tokenizer tokenizer, string systemPrompt, string userText, bool closeThinking)
    {
        var messages = ChatMessagesJson(systemPrompt, userText);

        string prompt;
        try
        {
            prompt = tokenizer.ApplyChatTemplate("", messages, "", true);
        }
        catch
        {
            prompt = $"<|system|>\n{systemPrompt}<|end|>\n<|user|>\n{userText}<|end|>\n<|assistant|>\n";
        }

        return closeThinking ? prompt + GenAiPack.ClosedThinkingBlock : prompt;
    }

    private static string ChatMessagesJson(string systemPrompt, string userText)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var (role, content) in new[] { ("system", systemPrompt), ("user", userText) })
            {
                writer.WriteStartObject();
                writer.WriteString("role", role);
                writer.WriteString("content", content);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
