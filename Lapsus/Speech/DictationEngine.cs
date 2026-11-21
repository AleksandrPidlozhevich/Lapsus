using Lapsus.Core.Models;
using Lapsus.Core.Speech;
using Lapsus.Core.Spelling;
using Lapsus.Input.Dictation;
using Lapsus.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Speech;

// The model loads when a recording starts (Prepare), not at app start — a second neural model sitting in memory all day is what dictation must not cost.
// Unloads after the user's idle time (SpeechUnloadAfter). Only "never unload" loads it ahead of time (Preload).
internal sealed class DictationEngine : IDictationEngine, IDisposable
{
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(10);

    // Mean token probability below which a CPU decode is tried again in the user's next language.
    private const float RetryBelow = 0.75f;

    private readonly WhisperRecognizer _recognizer = new();
    private readonly ParakeetRecognizer _parakeet = new();
    private readonly SpeechModelStore _store;
    private readonly Func<AppSettings> _settings;
    private readonly Func<IReadOnlyList<DictionarySource>> _dictionaries;
    private readonly object _gate = new();
    private readonly Timer _idleTimer;

    private Task? _load;
    private string? _loadKey;
    // The engine of the model being loaded or loaded; only one is in memory at a time.
    private volatile bool _onParakeet;
    private int _busy;
    private long _lastUseTicks = DateTime.UtcNow.Ticks;
    private (string Key, TermRestorer Restorer)? _restorer;
    private bool _disposed;

    public DictationEngine(
        SpeechModelStore store, Func<AppSettings> settings, Func<IReadOnlyList<DictionarySource>> dictionaries)
    {
        _store = store;
        _settings = settings;
        _dictionaries = dictionaries;
        _idleTimer = new Timer(_ => UnloadIfIdle(), null, IdleCheck, IdleCheck);
    }

    public bool HasModel => _store.IsInstalled(Model(_settings()));

    public bool IsLoaded => _onParakeet ? _parakeet.IsLoaded : _recognizer.IsLoaded;

    // Parakeet ~0.3 s a phrase on a CPU, Whisper ~0.4 s on a GPU; Whisper on a CPU (~2.5 s) would only queue up.
    public bool CanStream => _onParakeet ? _parakeet.IsLoaded : _recognizer.IsLoaded && _recognizer.OnGpu;

    public string DeviceLabel => _onParakeet ? (_parakeet.IsLoaded ? _parakeet.DeviceLabel : "") : _recognizer.DeviceLabel;

    // Raised on a worker thread once a load (and its warm-up) finishes.
    public event Action? Loaded;

    public void Prepare()
    {
        var settings = _settings();
        var model = Model(settings);
        if (!_store.IsInstalled(model))
            return;

        var preferGpu = settings.DictationDevice != ComputeDevicePreference.Cpu;
        var key = $"{model.Id}|{preferGpu}";
        lock (_gate)
        {
            if (_disposed || (_load is { IsFaulted: false, IsCanceled: false } && _loadKey == key && IsLoaded)
                || (_load is { IsCompleted: false } && _loadKey == key))
                return;

            _loadKey = key;
            var parakeet = _onParakeet = model.Engine == SpeechEngine.Parakeet;
            var path = _store.PathFor(model);
            var folder = _store.FolderFor(model);
            var vad = _store.VadPath;
            _load = Task.Run(async () =>
            {
                UnloadModels();
                if (parakeet)
                {
                    // CPU only, whatever DictationDevice says: on DirectML it was twice as slow.
                    _parakeet.Load(folder);
                    await _parakeet.WarmUpAsync().ConfigureAwait(false);
                }
                else
                {
                    _recognizer.Load(path, vad, preferGpu);
                    // On a GPU the first run compiles shaders (seconds, once per driver); do it now, not on the phrase.
                    await _recognizer.WarmUpAsync().ConfigureAwait(false);
                }
                Loaded?.Invoke();
            });
        }
    }

    public void Preload() => Prepare();

    public async Task<DictationResult> TranscribeAsync(float[] pcm, DictationRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _busy);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long waited = 0, decoded = 0, retried = 0, restored = 0;
        SpeechResult? first = null;
        var loading = false;
        try
        {
            loading = !IsLoaded;
            Prepare();
            Task? load;
            lock (_gate)
                load = _load;
            if (load is null)
                throw new InvalidOperationException("No speech model is installed.");

            // Waiting for the model is the queue, not a failure: the recording was taken while it loaded.
            await load.WaitAsync(cancellationToken).ConfigureAwait(false);
            waited = clock.ElapsedMilliseconds;

            var settings = _settings();
            var glossary = settings.DictationGlossary.Where(g => g.Trim().Length > 0).ToArray();
            // A model that hears one language is read in it, whatever else the user writes in.
            var fixedLanguage = Model(settings).Languages is { Count: 1 } only ? only.First() : settings.DictationLanguage;
            var among = fixedLanguage is not null
                ? [fixedLanguage]
                : DictationLanguagePicker.Candidates(request.Language, request.Languages);
            var language = fixedLanguage ?? request.Language;

            // Parakeet takes no prompt and picks the language itself; its text is taken to be in the main language,
            // for spoken punctuation and the term restorer.
            var parakeet = _onParakeet;
            Task<SpeechResult> Recognize(string lang, IReadOnlyList<string> detectAmong) => parakeet
                ? _parakeet.TranscribeAsync(pcm, new SpeechRequest(lang), cancellationToken)
                : _recognizer.TranscribeAsync(
                    pcm,
                    new SpeechRequest(lang, SpeechPromptBuilder.Build(lang, glossary.Select(g => g.Split('|')[0]),
                        request.PreviousText), detectAmong),
                    cancellationToken);

            var result = await Recognize(language, among).ConfigureAwait(false);
            first = result;
            decoded = clock.ElapsedMilliseconds;

            // A CPU skips language detection (a pass of its own, ~10 s), so speech in another of the user's
            // languages is decoded as the main one. Such a decode is unsure: ru speech read as en scored below
            // RetryBelow in 5 of its 8 broken phrases, ru read as ru never did (22 phrases, stage 2 audio).
            if (!parakeet && !_recognizer.OnGpu && result.MeanTokenProbability < RetryBelow
                && among.FirstOrDefault(l => l != result.Language) is { } other)
            {
                var second = await Recognize(other, [other]).ConfigureAwait(false);
                if (second.MeanTokenProbability > result.MeanTokenProbability)
                    result = second;
                retried = clock.ElapsedMilliseconds - decoded;
            }

            var text = HallucinationFilter.Clean(result.Text);
            if (text.Length > 0 && settings.DictationSpokenPunctuation)
                text = SpokenPunctuation.Apply(text, result.Language);
            if (text.Length > 0 && settings.KeepEnglishTermsLatin)
                text = await Task.Run(() => Restorer(glossary).Restore(text, result.Language), cancellationToken)
                    .ConfigureAwait(false);
            restored = clock.ElapsedMilliseconds;
            return new DictationResult(text, result.Language);
        }
        finally
        {
            // Timings only, never the text.
            var load = loading ? $"load {waited} ms (model was unloaded)" : $"model wait {waited} ms";
            if (!request.Live)
                CaptureLog.Write($"dictation: {DeviceLabel}, audio {pcm.Length / 16000.0:0.0} s, speech " +
                                 $"{first?.SpeechSeconds ?? 0:0.0} s, {first?.Language ?? "-"}; {load}, decode " +
                                 $"{Math.Max(decoded - waited, 0)} ms, retry {retried} ms, terms " +
                                 $"{Math.Max(restored - decoded - retried, 0)} ms, total {clock.ElapsedMilliseconds} ms");
            Interlocked.Exchange(ref _lastUseTicks, DateTime.UtcNow.Ticks);
            Interlocked.Decrement(ref _busy);
            if (_settings().SpeechUnloadAfter == SpeechUnloadAfter.Immediately)
                _ = Task.Run(UnloadIfIdle);
        }
    }

    public void Unload()
    {
        lock (_gate)
        {
            _load = null;
            _loadKey = null;
        }
        UnloadModels();
    }

    private void UnloadModels()
    {
        _recognizer.Unload();
        _parakeet.Unload();
    }

    private void UnloadIfIdle()
    {
        if (_disposed || Volatile.Read(ref _busy) > 0 || !IsLoaded)
            return;

        if (_settings().SpeechUnloadAfter.Delay() is not { } delay)
            return;

        var idle = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastUseTicks), DateTimeKind.Utc);
        if (idle < delay)
            return;

        lock (_gate)
        {
            // A dictation that began since the check keeps its model.
            if (_load is { IsCompleted: false } || Volatile.Read(ref _busy) > 0)
                return;
            _load = null;
            _loadKey = null;
        }
        UnloadModels();
    }

    // Built once per glossary and dictionary set; the English index inside takes a fifth of a second.
    private TermRestorer Restorer(string[] glossary)
    {
        var sources = _dictionaries();
        var key = string.Join('\n', glossary) + "\0" + string.Join('|', sources.Select(s => s.Path));
        if (_restorer is { } cached && cached.Key == key)
            return cached.Restorer;

        var restorer = new TermRestorer(new SpellChecker(sources), glossary);
        _restorer = (key, restorer);
        return restorer;
    }

    private static SpeechModel Model(AppSettings settings) =>
        SpeechModelCatalog.Find(settings.SpeechModelId) ?? SpeechModelCatalog.Default;

    public void AbandonForShutdown()
    {
        _disposed = true;
        _idleTimer.Dispose();
        _recognizer.AbandonForShutdown();
    }

    public void Dispose()
    {
        _disposed = true;
        _idleTimer.Dispose();
        _recognizer.Dispose();
        _parakeet.Dispose();
    }
}
