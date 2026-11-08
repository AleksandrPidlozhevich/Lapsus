using System;
using System.Collections.Generic;
using System.Linq;

namespace Lapsus.Input.Dictation;

// The last dictations' text, so one that never reached its field can still be copied. Kept in memory only, never
// written anywhere, and each entry is forgotten after Keep. UI thread.
internal sealed class DictationHistory(Func<DateTime> now)
{
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(30);

    private readonly List<(string Text, DateTime At)> _entries = [];

    public event Action? Changed;

    public void Add(string text)
    {
        Prune();
        _entries.Insert(0, (text, now()));
        Changed?.Invoke();
    }

    // Newest first.
    public IReadOnlyList<string> Recent()
    {
        Prune();
        return _entries.Select(e => e.Text).ToList();
    }

    private void Prune() => _entries.RemoveAll(e => now() - e.At >= Keep);
}
