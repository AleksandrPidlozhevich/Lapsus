using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Core.Models;
using Lapsus.Core.Spelling;
using Lapsus.Neural;
using Lapsus.Settings;
using Lapsus.ViewModels;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus;

#if MAS
#pragma warning disable CS9113 // modelStore serves only the neural path, which this edition leaves out.
#endif
internal sealed class BrainBuilder(
    IInputBackend backend, DictionaryStore dictionaryStore, ModelStore modelStore, WordExceptions exceptions)
{

    private readonly SemaphoreSlim _gate = new(1, 1);

#if !MAS
    private OnnxGenAiLlm? _llm = new();

    private (string Path, ComputeDevicePreference Device)? _loadedModel;
#endif

    private bool _shutDown;

    public SettingsViewModel? Settings { get; set; }

    // The neural brain's model while it is loaded, for dictation to judge doubtful terms with; it serializes its runs.
    public ILocalLlm? LoadedModel => _llm is { IsLoaded: true } llm ? llm : null;

    public async Task RebuildAsync()
    {
        if (_shutDown || Settings is null)
            return;

        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            // Lists installed before cleaning existed are cleaned before any brain reads them. Gated by version: no-op after the first run.
            await Task.Run(dictionaryStore.CleanOutdatedLists).ConfigureAwait(true);

            var settings = Settings.SettingsSnapshot();
            IPhraseCorrector corrector;

            backend.SetCorrector(new InactiveCorrector(settings.NeuralCorrectionEnabled));

#if MAS
            if (settings.PlainLayoutSwitch)
#else
            if (!settings.NeuralCorrectionEnabled)
                UnloadNeural();

            if (settings.NeuralCorrectionEnabled)
            {
                corrector = await BuildNeuralAsync(settings, Settings.Models);
            }
            else if (settings.PlainLayoutSwitch)
#endif
            {
                corrector = new PlainLayoutSwitcher(exceptions);
            }
            else
            {
                var installed = dictionaryStore.Installed();
                corrector = await Task
                    .Run(() => new LayoutCorrector(new SpellChecker(installed), exceptions: exceptions))
                    .ConfigureAwait(true);
            }

            backend.SetCorrector(corrector);

            backend.AutoMode = settings.AutoMode;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Native threads may still be decoding at exit, so the model is abandoned rather than freed.
    public void AbandonForShutdown()
    {
        _shutDown = true;
#if !MAS
        var llm = _llm;
        _llm = null;
        llm?.AbandonForShutdown();
#endif
    }

#if !MAS
    private void UnloadNeural()
    {
        _loadedModel = null;
        if (_llm is { } llm)
            _ = Task.Run(llm.Unload);
    }

    private async Task<IPhraseCorrector> BuildNeuralAsync(AppSettings settings, ModelsViewModel models)
    {
        var llm = _llm!;
        if (settings.SelectedModelId is not { } modelId || !modelStore.IsInstalled(modelId))
        {
            _loadedModel = null;
            await Task.Run(llm.Unload).ConfigureAwait(true);
            models.ReportNeedsModel();
            return new NeuralPhraseRewriter(llm, await BuildAdviserAsync(), exceptions);
        }

        var path = modelStore.PathFor(modelId);
        var display = modelStore.Describe(modelId)?.DisplayName ?? modelId;
        var plan = ExecutionPlan.Build(
            settings.ComputeDevice,
            ComputeDeviceProbe.Capabilities(),
            modelStore.InstalledVariant(modelId).Device,
            modelStore.DeclaredProviders(modelId));
        try
        {
            var wanted = (path, settings.ComputeDevice);
            if (!llm.IsLoaded || _loadedModel != wanted)
            {
                models.ReportLoading(display);
                _loadedModel = null;

                await Task.Run(() => llm.Load(path, plan)).ConfigureAwait(true);
                _loadedModel = wanted;

                _ = llm.WarmUpAsync();
            }

            var corrector = new NeuralPhraseRewriter(llm, await BuildAdviserAsync(), exceptions,
                spellingLanguages: SpellingLanguages.For(modelId));
            models.ReportReady(display, llm.ExecutionProvider);
            return corrector;
        }
        catch (Exception ex)
        {
            _loadedModel = null;
            await Task.Run(llm.Unload).ConfigureAwait(true);
            models.ReportLoadFailed(ex.Message);

            return new NeuralPhraseRewriter(llm, await BuildAdviserAsync(), exceptions);
        }
    }

    private async Task<IPhraseCorrector?> BuildAdviserAsync()
    {
        var installed = dictionaryStore.Installed();
        if (installed.Count == 0)
            return null;

        return await Task
            .Run(IPhraseCorrector () =>
                new LayoutCorrector(new SpellChecker(installed), exceptions: exceptions))
            .ConfigureAwait(true);
    }
#endif
}
