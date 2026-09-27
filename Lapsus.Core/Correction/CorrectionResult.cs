using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public readonly record struct CorrectionResult(
    string Original,
    string CorrectedString,
    double Confidence,
    bool Changed,
    KeyboardLayout? FromLayout,
    KeyboardLayout? ToLayout);

public readonly record struct PhraseCorrection(
    string Original,
    string Corrected,
    bool Changed,
    KeyboardLayout? TargetLayout,
    string? TargetLayoutId = null,
    IReadOnlyList<TextSpan>? Settled = null);

public sealed record LayoutSource(Script? Script, string? LanguageCode, KeyboardMap Map, string? LayoutId = null);

public readonly record struct LayoutCandidate(
    Script ScoringScript,
    KeyboardLayout? Target,
    KeyboardMap Map,
    string? LanguageCode = null,
    string? LayoutId = null);
