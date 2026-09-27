using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

internal sealed class TargetVotes
{
    private readonly Dictionary<string, Vote> _votes = new(StringComparer.Ordinal);

    public void Add(KeyboardLayout? layout, string? layoutId, bool counter = false)
    {
        var key = layout?.ToString() ?? layoutId;
        if (key is null)
            return;

        _votes[key] = _votes.TryGetValue(key, out var existing)
            ? new Vote(layout ?? existing.Layout, existing.LayoutId ?? layoutId,
                existing.Count + 1, existing.Elects || !counter)
            : new Vote(layout, layoutId, 1, !counter);
    }

    public (KeyboardLayout? Layout, string? LayoutId) Pick(KeyboardLayout? preferred)
    {
        var best = default(Vote);
        foreach (var vote in _votes.Values)
            if (Outranks(vote, best))
                best = vote;

        if (!best.Elects)
            return (null, null);

        if (preferred is { } p)
            foreach (var vote in _votes.Values)
                if (vote.Elects && vote.Layout == p && vote.Count == best.Count)
                    return (vote.Layout, vote.LayoutId);

        return (best.Layout, best.LayoutId);
    }

    private static bool Outranks(in Vote candidate, in Vote incumbent)
    {
        return candidate.Count > incumbent.Count ||
               (candidate.Count == incumbent.Count && candidate.Elects && !incumbent.Elects);
    }

    private readonly record struct Vote(KeyboardLayout? Layout, string? LayoutId, int Count, bool Elects);
}
