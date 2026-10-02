using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

// Cancellation reaches a brain that asks a model: a second hotkey press or a brain swap ends the first
// request instead of leaving it to hold the model.
public readonly record struct CorrectionHints(
    Script? LineDirection = null, bool KeysOnly = false, CancellationToken Cancellation = default);
