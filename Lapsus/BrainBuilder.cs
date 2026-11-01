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

// Picks the correction brain from settings and hands it to the backend, one rebuild at a time.
internal sealed class BrainBuilder(
    IInputBackend backend, DictionaryStore dictionaryStore, ModelStore modelStore, WordExceptions exceptions)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private OnnxGenAiLlm? _llm = new();

    private (string Path, ComputeDevicePreference Device)? _loadedModel;

    // Set once the settings view model exists; it is built with RebuildAsync as its callback.
    public SettingsViewModel? Settings { get; set; }

    public async Task RebuildAsync()
    {
        if (_llm is null || Settings is null)
            return;

        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            // Lists installed before cleaning existed are cleaned before any brain reads them. Gated by version: no-op after the first run.
            await Task.Run(dictionaryStore.CleanOutdatedLists).ConfigureAwait(true);

            var settings = Settings.SettingsSnapshot();
            IPhraseCorrector corrector;

            backend.SetCorrector(new InactiveCorrector(settings.NeuralCorrectionEnabled));

            if (!settings.NeuralCorrectionEnabled)
            {
                _loadedModel = null;
                _ = Task.Run(() => _llm.Unload());
            }

            if (settings.NeuralCorrectionEnabled)
            {
                if (settings.SelectedModelId is { } modelId && modelStore.IsInstalled(modelId))
                {
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
                        if (!_llm.IsLoaded || _loadedModel != wanted)
                        {
                            Settings.Models.ReportLoading(display);
                            _loadedModel = null;

                            await Task.Run(() => _llm.Load(path, plan)).ConfigureAwait(true);
                            _loadedModel = wanted;

                            WarmUpNeural(backend.PreferredLayout);
                        }

                        corrector = new NeuralPhraseRewriter(_llm, await BuildAdviserAsync(), exceptions,
                            spellingLanguages: SpellingLanguages.For(modelId));
                        Settings.Models.ReportReady(display, _llm.ExecutionProvider);
                    }
                    catch (Exception ex)
                    {
                        _loadedModel = null;
                        await Task.Run(() => _llm.Unload()).ConfigureAwait(true);
                        Settings.Models.ReportLoadFailed(ex.Message);

                        corrector = new NeuralPhraseRewriter(_llm, await BuildAdviserAsync(), exceptions);
                    }
                }
                else
                {
                    _loadedModel = null;
                    await Task.Run(() => _llm.Unload()).ConfigureAwait(true);
                    Settings.Models.ReportNeedsModel();
                    corrector = new NeuralPhraseRewriter(_llm, await BuildAdviserAsync(), exceptions);
                }
            }
            else if (settings.PlainLayoutSwitch)
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
        var llm = _llm;
        _llm = null;
        llm?.AbandonForShutdown();
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

    private void WarmUpNeural(KeyboardLayout? preferred)
    {
        if (_llm is not { } llm)
            return;

        const string sample = "ntrcn";
        var system = NeuralRewritePrompt.BuildSystem(preferred);
        var user = NeuralRewritePrompt.BuildUser(
            sample, BundledKeyboardMaps.En, backend.LayoutCandidates(), preferred);
        _ = llm.WarmUpAsync(system, user, sample.Length);
    }
}
