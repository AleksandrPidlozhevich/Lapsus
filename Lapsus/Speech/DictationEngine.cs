using Lapsus.Core.Correction;
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
// The model follows the layout: a dictation runs on the chosen model if it hears the active layout's language, else
// on a downloaded one that does (SpeechModelCatalog.ForLanguage). With 16 GB of memory two stay loaded, so
// switching between a Georgian and a Russian layout does not reload each time; Whisper models share one recognizer,
// so only one of those. Each unloads after its own idle time: one used in the morning does not sit there all day.
internal sealed class DictationEngine : IDictationEngine, IDisposable
{
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(10);
    private static readonly int MaxLoaded = Capacity(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    // Mean token probability below which a CPU decode is tried again in the user's next language.
    private const float RetryBelow = 0.75f;

    private readonly WhisperRecognizer _recognizer = new();
    private readonly SpeechModelStore _store;
    private readonly Func<AppSettings> _settings;
    private readonly Func<IReadOnlyList<DictionarySource>> _dictionaries;
    private readonly Func<ILocalLlm?> _llm;
    private readonly object _gate = new();
    private readonly Timer _idleTimer;

    // Loaded or loading, least recently used first; _current is the last one a dictation asked for.
    private readonly List<Slot> _slots = [];
    private volatile Slot? _current;
    private int _busy;
    private (string Key, TermRestorer Restorer)? _restorer;
    private bool _disposed;

    public DictationEngine(
        SpeechModelStore store, Func<AppSettings> settings, Func<IReadOnlyList<DictionarySource>> dictionaries,
        Func<ILocalLlm?>? llm = null)
    {
        _llm = llm ?? (() => null);
        _store = store;
        _settings = settings;
        _dictionaries = dictionaries;
        _idleTimer = new Timer(_ => UnloadIfIdle(), null, IdleCheck, IdleCheck);
    }

    public bool HasModel => _store.IsInstalled(Model(_settings()));

    public bool IsLoaded => _current is { } slot && (slot.Nemo?.IsLoaded ?? _recognizer.IsLoaded);

    // Parakeet ~0.3 s a phrase on a CPU, Whisper ~0.4 s on a cheap GPU; Metal and CPU are too slow to stream.
    public bool CanStream => _current is { } slot
                             && (slot.Nemo?.IsLoaded ?? (_recognizer.IsLoaded && _recognizer.FullWindowIsCheap));

    public string DeviceLabel => _current switch
    {
        null => "",
        { Nemo: { } nemo } => nemo.IsLoaded ? nemo.DeviceLabel : "",
        _ => _recognizer.DeviceLabel,
    };

    // Raised on a worker thread once a load (and its warm-up) finishes.
    public event Action? Loaded;

    // Nemo: the model's own NeMo recognizer; null for Whisper, which runs on the shared _recognizer.
    // A class, not a record: slots are told apart by identity.
    private sealed class Slot(SpeechModel model, bool preferGpu, NemoRecognizer? nemo)
    {
        public SpeechModel Model { get; } = model;
        public bool PreferGpu { get; } = preferGpu;
        public NemoRecognizer? Nemo { get; } = nemo;
        public Task Load { get; set; } = Task.CompletedTask;

        // Ticks of the last dictation that used it, or of its load.
        public long LastUse = DateTime.UtcNow.Ticks;
    }

    // A second model costs 0.7–1.1 GB; with less than 16 GB (15 as Windows reports it) one stays, the other reloads.
    internal static int Capacity(long memoryBytes) => memoryBytes >= 15L << 30 ? 2 : 1;

    internal static bool IsIdle(long lastUseTicks, DateTime now, TimeSpan delay) =>
        now - new DateTime(lastUseTicks, DateTimeKind.Utc) >= delay;

    public void Prepare(string? language = null) => Start(Pick(_settings(), language));

    private SpeechModel Pick(AppSettings settings, string? language)
    {
        var chosen = Model(settings);
        return (settings.DictationLanguage ?? language) is { } lang
            ? SpeechModelCatalog.ForLanguage(chosen, DictationLanguagePicker.Normalize(lang), _store.IsInstalled)
            : chosen;
    }

    // The model's slot, loading it unless it is loaded or loading; null when it is not on disk.
    private Slot? Start(SpeechModel model)
    {
        if (!_store.IsInstalled(model))
            return null;

        var preferGpu = _settings().DictationDevice != ComputeDevicePreference.Cpu;
        lock (_gate)
        {
            if (_disposed)
                return null;
            var found = _slots.FirstOrDefault(s => s.Model.Id == model.Id && s.PreferGpu == preferGpu
                                                   && s.Load is { IsFaulted: false, IsCanceled: false });
            if (found is not null)
            {
                Interlocked.Exchange(ref found.LastUse, DateTime.UtcNow.Ticks);
                _slots.Remove(found);
                _slots.Add(found);
                return _current = found;
            }

            var whisper = model.Engine == SpeechEngine.Whisper;
            // busy is 1 for the session Hold's own Prepare (eviction is fine). Above that a Transcribe is in
            // flight: language detection must not unload its recognizer. Whisper shares one native instance, so
            // keep the slot already loaded rather than Load-replacing it under a running decode.
            var protectInFlight = Volatile.Read(ref _busy) > 1;
            if (protectInFlight && whisper
                && _slots.LastOrDefault(s => s.Nemo is null && s.Load is { IsFaulted: false, IsCanceled: false })
                    is { } whispering)
            {
                Interlocked.Exchange(ref whispering.LastUse, DateTime.UtcNow.Ticks);
                return _current = whispering;
            }

            var evicted = new List<Slot>();
            if (!protectInFlight)
            {
                // The same model on another device, the other Whisper, then the least recently used past MaxLoaded.
                evicted = _slots.Where(s => s.Model.Id == model.Id || (whisper && s.Nemo is null)).ToList();
                _slots.RemoveAll(evicted.Contains);
                while (_slots.Count >= MaxLoaded)
                {
                    evicted.Add(_slots[0]);
                    _slots.RemoveAt(0);
                }
            }

            var slot = new Slot(model, preferGpu, whisper ? null : new NemoRecognizer());
            var path = _store.PathFor(model);
            var folder = _store.FolderFor(model);
            var vad = _store.VadPath;
            slot.Load = Task.Run(async () =>
            {
                await Release(evicted).ConfigureAwait(false);
                if (slot.Nemo is { } nemo)
                {
                    // CPU only, whatever DictationDevice says: on DirectML it was twice as slow.
                    nemo.Load(folder);
                    await nemo.WarmUpAsync().ConfigureAwait(false);
                }
                else
                {
                    _recognizer.Load(path, vad, preferGpu);
                    // On a GPU the first run compiles shaders (seconds, once per driver); do it now, not on the phrase.
                    await _recognizer.WarmUpAsync().ConfigureAwait(false);
                }
                Loaded?.Invoke();
            });
            _slots.Add(slot);
            return _current = slot;
        }
    }

    // After their own loads settle, so an unload never races a load; a recognition in flight finishes first (the
    // recognizers unload under their own lock). The shared Whisper recognizer is left to a newer Whisper slot, whose
    // Load replaces the model, so a late release cannot unload what that slot just loaded.
    private async Task Release(IEnumerable<Slot> slots)
    {
        foreach (var slot in slots)
        {
            await slot.Load.ContinueWith(_ => { }, TaskScheduler.Default).ConfigureAwait(false);
            if (slot.Nemo is { } nemo)
            {
                // Unload waits out a recognition in flight; the slot is gone, so the recognizer goes with it.
                nemo.Unload();
                nemo.Dispose();
            }
            else
            {
                bool replaced;
                lock (_gate)
                    replaced = _slots.Any(s => s.Nemo is null);
                if (!replaced)
                    _recognizer.Unload();
            }
        }
    }

    public void Preload() => Prepare();

    public IDisposable Hold(string? language = null)
    {
        Interlocked.Increment(ref _busy);
        Prepare(language);
        return new ModelHold(this);
    }

    private void ReleaseHold()
    {
        if (_current is { } slot)
            Interlocked.Exchange(ref slot.LastUse, DateTime.UtcNow.Ticks);
        Interlocked.Decrement(ref _busy);
        if (_settings().SpeechUnloadAfter == SpeechUnloadAfter.Immediately)
            _ = Task.Run(UnloadIfIdle);
    }

    private sealed class ModelHold(DictationEngine engine) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                engine.ReleaseHold();
        }
    }

    public async Task<DictationResult> TranscribeAsync(float[] pcm, DictationRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _busy);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long waited = 0, decoded = 0, retried = 0, restored = 0;
        SpeechResult? first = null;
        SpeechModel? model = null;
        Slot? slot = null;
        var loading = false;
        try
        {
            var settings = _settings();
            model = Pick(settings, request.Language);
            slot = Start(model) ?? throw new InvalidOperationException("No speech model is installed.");
            loading = !slot.Load.IsCompleted;

            // Waiting for the model is the queue, not a failure: the recording was taken while it loaded.
            await slot.Load.WaitAsync(cancellationToken).ConfigureAwait(false);
            waited = clock.ElapsedMilliseconds;

            var glossary = settings.DictationGlossary.Where(g => g.Trim().Length > 0).ToArray();
            // A model that hears one language is read in it, whatever else the user writes in.
            var fixedLanguage = model.Languages is { Count: 1 } only ? only.First() : settings.DictationLanguage;
            var among = fixedLanguage is not null
                ? [fixedLanguage]
                : DictationLanguagePicker.Candidates(request.Language, request.Languages);
            var language = fixedLanguage ?? request.Language;

            // A NeMo model takes no prompt and picks the language itself; its text is taken to be in the main language,
            // for spoken punctuation and the term restorer.
            var nemo = slot.Nemo;
            Task<SpeechResult> Recognize(string lang, IReadOnlyList<string> detectAmong) => nemo is not null
                ? nemo.TranscribeAsync(pcm, new SpeechRequest(lang), cancellationToken)
                : _recognizer.TranscribeAsync(
                    pcm,
                    new SpeechRequest(lang, SpeechPromptBuilder.Build(lang, glossary.Select(g => g.Split('|')[0]),
                        request.PreviousText), detectAmong),
                    cancellationToken);

            var result = await Recognize(language, among).ConfigureAwait(false);
            first = result;
            decoded = clock.ElapsedMilliseconds;

            // Without a fast GPU language detection is skipped (a pass of its own, 5-10 s), so speech in another of the user's
            // languages is decoded as the main one. Such a decode is unsure: ru speech read as en scored below
            // RetryBelow in 5 of its 8 broken phrases, ru read as ru never did (22 phrases, stage 2 audio).
            // A word or two scores low whatever the language, and a second pass doubles its wait (1.5 → 3 s on an
            // M1) while it won 1 of 10 words, so a short one stays in the main language.
            if (nemo is null && !_recognizer.FullWindowIsCheap && result.MeanTokenProbability < RetryBelow
                && result.SpeechSeconds >= WhisperRecognizer.OneWord.TotalSeconds
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
            {
                var restorer = Restorer(glossary);
                text = await Task.Run(() => restorer.Restore(text, result.Language), cancellationToken)
                    .ConfigureAwait(false);
                // With the neural brain loaded, the words no dictionary knows that sound near a term go to its model:
                // on the 22 ru phrases Parakeet's terms went 12/25 → 16/25, at ~0.2 s a guess (Qwen3 0.6B, CPU).
                // Not for live words: those run every 300 ms, and the part's own recognition comes through here.
                if (!request.Live && _llm() is { } llm && restorer.Guesses(text, result.Language) is { Count: > 0 } guesses)
                    text = await new DictationTermRanker(llm).RankAsync(text, guesses, cancellationToken).ConfigureAwait(false);
            }
            restored = clock.ElapsedMilliseconds;
            return new DictationResult(text, result.Language);
        }
        finally
        {
            // Timings only, never the text.
            var load = loading ? $"load {waited} ms (model was unloaded)" : $"model wait {waited} ms";
            if (!request.Live)
                CaptureLog.Write($"dictation: {model?.Id}, {DeviceLabel}, audio {pcm.Length / 16000.0:0.0} s, speech " +
                                 $"{first?.SpeechSeconds ?? 0:0.0} s, {first?.Language ?? "-"}; {load}, decode " +
                                 $"{Math.Max(decoded - waited, 0)} ms, retry {retried} ms, terms " +
                                 $"{Math.Max(restored - decoded - retried, 0)} ms, total {clock.ElapsedMilliseconds} ms");
            if (slot is not null)
                Interlocked.Exchange(ref slot.LastUse, DateTime.UtcNow.Ticks);
            Interlocked.Decrement(ref _busy);
            if (_settings().SpeechUnloadAfter == SpeechUnloadAfter.Immediately)
                _ = Task.Run(UnloadIfIdle);
        }
    }

    public void Unload()
    {
        // Cancel has already been asked; wait out in-flight recognition so a shared Whisper/Nemo unload
        // does not race a still-running part (same budget as WhisperRecognizer.Unload).
        var deadline = Environment.TickCount64 + 20_000;
        while (Volatile.Read(ref _busy) > 0 && Environment.TickCount64 < deadline)
            Thread.Sleep(20);

        List<Slot> slots;
        lock (_gate)
        {
            slots = [.. _slots];
            _slots.Clear();
            _current = null;
        }
        Release(slots).GetAwaiter().GetResult();
    }

    // Each model by its own last use; one still loading, or any while a dictation runs, stays.
    private void UnloadIfIdle()
    {
        if (_disposed || Volatile.Read(ref _busy) > 0 || _settings().SpeechUnloadAfter.Delay() is not { } delay)
            return;

        var now = DateTime.UtcNow;
        List<Slot> idle;
        lock (_gate)
        {
            if (Volatile.Read(ref _busy) > 0)
                return;
            idle = _slots.Where(s => s.Load.IsCompleted && IsIdle(Interlocked.Read(ref s.LastUse), now, delay)).ToList();
            if (idle.Count == 0)
                return;
            _slots.RemoveAll(idle.Contains);
            if (_current is { } current && idle.Contains(current))
                _current = _slots.LastOrDefault();
        }
        Release(idle).GetAwaiter().GetResult();
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
        // Leave Nemo models where they are: disposing under a still-running decode is worse than leaking on exit.
        lock (_gate)
        {
            _slots.Clear();
            _current = null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _idleTimer.Dispose();
        _recognizer.Dispose();
        List<Slot> slots;
        lock (_gate)
        {
            slots = [.. _slots];
            _slots.Clear();
            _current = null;
        }
        foreach (var slot in slots)
            slot.Nemo?.Dispose();
    }
}
