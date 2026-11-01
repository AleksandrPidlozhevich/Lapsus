using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Core.Text;
using Lapsus.Input.Dictation;
using Lapsus.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input;

// Selection actions: copy the selection, fix or transform it, paste it back.
internal abstract partial class InputBackendBase<TFocus>
{
    protected async Task ApplySelectionActionAsync(SelectionAction action)
    {

        if (_selectionActionRunning)
            return;

        if (InputBlockedReason() is { } blocked)
        {
            RaiseDiagnostic(blocked);
            return;
        }

        if (action == SelectionAction.Correct && !Corrector.IsReady)
        {
            RaiseDiagnostic(Localizer.Instance[Corrector.PreferAsync ? "Diag_NoModel" : "Diag_NoDictionaries"]);
            return;
        }

        _selectionActionRunning = true;
        try
        {
            string? selection;
            try
            {
                selection = await CopySelectionAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                RaiseDiagnosticFormat("Diag_ClipboardFailed", ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(selection))
            {
                RaiseDiagnosticFormat("Diag_NoSelection", ForegroundAppName());
                return;
            }

            try
            {
                if (action != SelectionAction.Correct)
                {
                    ApplySelectionTransform(action, selection);
                    return;
                }

                var layouts = ReadLayouts();
                var generation = _asyncCorrectionGeneration;
                var request = BeginModelRequest();
                var hints = new CorrectionHints(Cancellation: request.Token);
                (PhraseCorrection Phrase, InstalledLayout? Source) result;
                try
                {
                    result = await Task.Run(
                        () => (Phrase: CorrectWith(selection, layouts.Active, layouts.All, hints),
                            Source: layouts.Active)).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    EndModelRequest(request);
                }

                if (generation != _asyncCorrectionGeneration)
                    return;

                if (!result.Phrase.Changed)
                {
                    RaiseDiagnosticFormat("Diag_NoChange", Shorten(selection));
                    return;
                }

                if (!PasteText(result.Phrase.Corrected))
                    return;

                RaiseCorrected(Shorten(selection), Shorten(result.Phrase.Corrected),
                    result.Source?.Target, result.Phrase.TargetLayout);
                SwitchLayoutIfRequested(result.Phrase.TargetLayout, result.Phrase.TargetLayoutId);
            }
            catch (Exception ex)
            {
                RaiseDiagnosticFormat(Corrector.PreferAsync ? "Diag_NeuralFailed" : "Diag_CorrectionFailed", ex.Message);
            }
        }
        finally
        {

            try
            {
                await RestoreClipboardAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                RaiseDiagnosticFormat("Diag_ClipboardFailed", ex.Message);
            }
            finally
            {
                _selectionActionRunning = false;
            }
        }
    }

    private void ApplySelectionTransform(SelectionAction action, string selection)
    {

        var (latinTarget, language) = action == SelectionAction.Transliterate
            ? LatinTransliterationTarget()
            : (Script.Cyrillic, null);

        var transformed = SelectionTransforms.Apply(action, selection, latinTarget, language);
        if (string.Equals(transformed, selection, StringComparison.Ordinal))
        {
            RaiseDiagnosticFormat("Diag_NoChange", Shorten(selection));
            return;
        }

        if (!PasteText(transformed))
            return;

        RaiseCorrected(Shorten(selection), Shorten(transformed), null, null);
    }

    private (Script Script, string? Language) LatinTransliterationTarget()
    {

        switch (PreferredLayout)
        {
            case KeyboardLayout.El: return (Script.Greek, "el");
            case KeyboardLayout.He: return (Script.Hebrew, "he");
            case KeyboardLayout.Ar: return (Script.Arabic, "ar");
        }

        var preferredLanguage = PreferredLayout is { } preferred ? LayoutLanguage.FromKeyboardLayout(preferred) : null;

        Script? other = null;
        foreach (var layout in ReadLayouts().All)
        {
            if (layout.Script == Script.Cyrillic)
                return (Script.Cyrillic, preferredLanguage ?? layout.LanguageCode);

            if (layout.Script is Script.Greek or Script.Hebrew or Script.Arabic)
                other ??= layout.Script;
        }

        return other switch
        {
            Script.Greek => (Script.Greek, "el"),
            Script.Hebrew => (Script.Hebrew, "he"),
            Script.Arabic => (Script.Arabic, "ar"),
            _ => (Script.Cyrillic, preferredLanguage)
        };
    }

    private static string Shorten(string text)
    {
        const int limit = 60;
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= limit ? flat : flat[..limit] + "…";
    }
}
