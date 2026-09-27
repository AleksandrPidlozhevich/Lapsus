using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public readonly record struct CorrectionHints(Script? LineDirection = null, bool KeysOnly = false);
