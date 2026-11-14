using Lapsus.Core.Correction;
using Lapsus.Settings;
using System.Collections.Generic;

namespace Lapsus.Input;

// A word whose auto fix the user keeps taking back is not a slip: after a few times it goes on the
// leave-alone list, where the Apps tab shows it and it can be removed.
internal sealed class AutoFixRejections(AppSettings settings, SettingsStore store, WordExceptions exceptions)
{
    public const int Limit = 3;

    // shortcut: the counts are dropped wholesale at this size, evict the oldest instead if it ever matters.
    private const int MaxTracked = 200;

    public void Note(string word)
    {
        var counts = settings.RejectedAutoFixes;
        var count = counts.GetValueOrDefault(word) + 1;
        if (count >= Limit)
        {
            counts.Remove(word);
            exceptions.Add(word);
        }
        else
        {
            if (counts.Count >= MaxTracked)
                counts.Clear();
            counts[word] = count;
        }

        store.Save(settings);
    }
}
