using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

// Cancellation reaches a brain that asks a model: a second hotkey press or a brain swap ends the first
// request instead of leaving it to hold the model.
// TypoOnly: fix spelling inside the typed layout only; no word may be read as another layout's text.
public readonly record struct CorrectionHints(
    Script? LineDirection = null, bool KeysOnly = false, CancellationToken Cancellation = default,
    bool TypoOnly = false);
