using Lapsus.Core.Correction;
using Lapsus.Core.Models;
using Microsoft.ML.OnnxRuntimeGenAI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Neural;

public sealed class OnnxGenAiLlm : ILocalLlm
{
    private static readonly TimeSpan UnloadIdleTimeout = TimeSpan.FromSeconds(20);

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _idle = new(true);

    private readonly SemaphoreSlim _runGate = new(1, 1);

    private CancellationTokenSource _runCts = new();
    private Model? _model;
    private Tokenizer? _tokenizer;
    private OnnxTextGenerator? _generator;
    private int _activeCompletions;
    private bool _disposed;

    public ComputeDevice Device { get; private set; } = ComputeDevice.Cpu;

    public string ExecutionProvider { get; private set; } = "";

    public bool IsLoaded
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _model is not null && _tokenizer is not null;
            }
        }
    }

    public void Load(string modelDirectory, IReadOnlyList<ExecutionAttempt> plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        ArgumentNullException.ThrowIfNull(plan);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Unload();

        var session = OnnxModelLoader.Load(modelDirectory, plan);

        lock (_gate)
        {
            if (_disposed)
            {
                OnnxModelLoader.SafeDispose(session.Tokenizer);
                OnnxModelLoader.SafeDispose(session.Model);
                throw new ObjectDisposedException(nameof(OnnxGenAiLlm));
            }

            _model = session.Model;
            _tokenizer = session.Tokenizer;
            _generator = new OnnxTextGenerator(session.Model, session.Tokenizer, session.ClosesThinking);
            Device = session.Attempt.Device;
            ExecutionProvider = ComputeDeviceProbe.Label(session.Attempt.Provider);
        }
    }

    public void Unload()
    {
        Model? model;
        Tokenizer? tokenizer;
        OnnxTextGenerator? generator;
        CancellationTokenSource oldCts;

        lock (_gate)
        {
            oldCts = _runCts;
            _runCts = new CancellationTokenSource();

            tokenizer = _tokenizer;
            model = _model;
            generator = _generator;
            _tokenizer = null;
            _model = null;
            _generator = null;
            ExecutionProvider = "";
            Device = ComputeDevice.Cpu;
        }

        try
        {
            oldCts.Cancel();
        }
        catch (ObjectDisposedException)
        {

        }

        _idle.Wait(UnloadIdleTimeout);

        OnnxModelLoader.SafeDispose(generator);
        OnnxModelLoader.SafeDispose(tokenizer);
        OnnxModelLoader.SafeDispose(model);

        try
        {
            oldCts.Dispose();
        }
        catch
        {

        }
    }

    public Task<string> CompleteAsync(
        string systemPrompt,
        string userText,
        int answerChars,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            (generator, ct) => generator.Complete(systemPrompt, userText, answerChars, ct),
            cancellationToken);
    }

    public async Task<IReadOnlyList<double>?> ScoreAsync(
        string context, IReadOnlyList<string> texts, bool ends, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return [];

        return await RunAsync(
                (generator, ct) => generator.Score(context, texts, ends, ct),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> WarmUpAsync(
        string systemPrompt,
        string userText,
        int answerChars,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsLoaded)
            return false;

        try
        {
            await RunAsync<object?>(
                    (generator, ct) =>
                    {
                        generator.WarmUp(systemPrompt, userText, answerChars, ct);
                        return null;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException
                                       or ObjectDisposedException)
        {
            return false;
        }
    }

    private async Task<T> RunAsync<T>(
        Func<OnnxTextGenerator, CancellationToken, T> work, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        OnnxTextGenerator generator;
        CancellationToken runToken;
        lock (_gate)
        {
            if (_disposed || _generator is null)
                throw new InvalidOperationException("No ONNX model is loaded.");
            generator = _generator;
            runToken = _runCts.Token;
            if (Interlocked.Increment(ref _activeCompletions) == 1)
                _idle.Reset();
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runToken);
        var ct = linked.Token;

        try
        {
            await _runGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await Task.Run(() => work(generator, ct), ct).ConfigureAwait(false);
            }
            finally
            {
                _runGate.Release();
            }
        }
        finally
        {
            if (Interlocked.Decrement(ref _activeCompletions) == 0)
                _idle.Set();
        }
    }

    public void AbandonForShutdown()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            cts = _runCts;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {

        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        Unload();
        _idle.Dispose();
        _runGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
