using System;
using System.Collections.Generic;
using System.Threading;

namespace Lapsus.Input;

public sealed class AppExclusions
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    public event EventHandler? Changed;

    public AppExclusions()
    {
    }

    public AppExclusions(IEnumerable<string> names)
    {
        foreach (var name in names)
            if (Normalize(name) is { } normalized)
                _names.Add(normalized);
    }

    // Cheap and lock-free enough to read from the macOS tap callback: with nothing excluded, a
    // hotkey needs no pid classification at all to know it is not excluded.
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
                return _names.Count == 0;
        }
    }

    public bool Contains(string? processName)
    {
        if (processName is null || Normalize(processName) is not { } normalized)
            return false;

        lock (_gate)
        {
            return _names.Count > 0 && _names.Contains(normalized);
        }
    }

    public bool Add(string name)
    {
        if (Normalize(name) is not { } normalized)
            return false;

        lock (_gate)
        {
            if (!_names.Add(normalized))
                return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Remove(string name)
    {
        if (Normalize(name) is not { } normalized)
            return false;

        lock (_gate)
        {
            if (!_names.Remove(normalized))
                return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            var all = new List<string>(_names);
            all.Sort(StringComparer.Ordinal);
            return all;
        }
    }

    private static string? Normalize(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return null;

        var slash = trimmed.LastIndexOfAny(['\\', '/']);
        if (slash >= 0)
            trimmed = trimmed[(slash + 1)..];

        foreach (var extension in Extensions)
            if (trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^extension.Length];
                break;
            }

        return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
    }

    private static readonly string[] Extensions = [".exe", ".app"];
}
