using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // Each text is fed after the context as plain text, no chat template: small models read which of
    // several spellings is natural far better than they follow a rewrite instruction. Tokens the texts
    // share stay in the KV cache; only the rest is run per text.
    public double[] Score(string context, IReadOnlyList<string> texts, bool ends, CancellationToken cancellationToken)
    {
        var sequences = new int[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
            sequences[i] = EncodeRaw(context + texts[i]);

        // The first scored token needs the row of the one before it, so that one is run again.
        var shared = sequences[0].Length;
        foreach (var sequence in sequences)
            shared = Math.Min(shared, Math.Min(SharedPrefix(sequences[0], sequence), sequence.Length - 1));
        var kept = Math.Max(shared - 1, 0);
        var eos = _tokenizer.GetEosTokenIds().ToArray();

        var scores = new double[texts.Count];
        var reusable = !_reuseRefused && sequences.Max(s => s.Length) + 1 <= ReusableMaxLength;
        for (var i = 0; i < sequences.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reusable)
                try
                {
                    var generator = Keep(sequences[i], kept);
                    generator.AppendTokens(sequences[i].AsSpan(kept));
                    _cached = sequences[i];
                    scores[i] = LogProbability(generator, sequences[i], kept, ends, eos);
                    continue;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RefuseReuse();
                    reusable = false;
                }

            using var generatorParams = NewParams(sequences[i].Length + 1);
            using var once = new Generator(_model, generatorParams);
            once.AppendTokens(sequences[i]);
            scores[i] = LogProbability(once, sequences[i], kept, ends, eos);
        }

        return scores;
    }

    // A generator holding exactly the first `count` tokens of the sequence in its cache.
    private Generator Keep(int[] sequence, int count)
    {
        if (_generator is not null && SharedPrefix(_cached, sequence) >= count)
        {
            _generator.RewindTo((ulong)count);
            _cached = sequence[..count];
            return _generator;
        }

        if (count == 0)
        {
            DropGenerator();
            _params = NewParams(ReusableMaxLength);
            _generator = new Generator(_model, _params);
            return _generator;
        }

        return Prefill(sequence[..count], CancellationToken.None);
    }

    // Rows of the last append: row r was fed sequence[from + r] and predicts sequence[from + r + 1].
    private static double LogProbability(Generator generator, int[] sequence, int from, bool ends, int[] eos)
    {
        using var logits = generator.GetOutput("logits");
        var shape = logits.Shape();
        var rows = (int)shape[1];
        var vocabulary = (int)shape[2];
        var data = logits.Type() == ElementType.float16
            ? Array.ConvertAll(logits.GetData<Half>().ToArray(), h => (float)h)
            : logits.GetData<float>().ToArray();

        // Rows cover only the tokens appended last; a cache hit may have skipped the head.
        var first = sequence.Length - rows;
        var total = 0.0;
        for (var r = 0; r < rows - 1; r++)
            if (first + r + 1 > from)
                total += LogSoftmaxAt(data.AsSpan(r * vocabulary, vocabulary), sequence[first + r + 1]);

        if (ends && eos.Length > 0)
        {
            var last = data.AsSpan((rows - 1) * vocabulary, vocabulary);
            var best = double.NegativeInfinity;
            foreach (var id in eos)
                best = Math.Max(best, LogSoftmaxAt(last, id));
            total += best;
        }

        return total;
    }

    private static double LogSoftmaxAt(ReadOnlySpan<float> row, int id)
    {
        var max = float.NegativeInfinity;
        foreach (var value in row)
            if (value > max)
                max = value;

        var sum = 0.0;
        foreach (var value in row)
            sum += Math.Exp(value - max);

        return row[id] - max - Math.Log(sum);
    }

    private int[] EncodeRaw(string text)
    {
        using var sequences = _tokenizer.Encode(text);
        return sequences[0].ToArray();
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
