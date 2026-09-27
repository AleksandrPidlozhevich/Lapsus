using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

internal readonly record struct WordSource(
    Script Script,
    KeyboardMap Map,
    IReadOnlyList<LayoutCandidate> Candidates,
    string? LanguageCode);
