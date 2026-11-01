using Lapsus.Core.Layout;
using SymSpellEngine = SymSpell;

namespace Lapsus.Core.Spelling;

// One language's frequency list, built once per file by DictionaryFiles and shared between SpellCheckers.
internal readonly record struct LoadedDictionary(
    Dictionary<string, long> Counts, SymSpellEngine Engine, double LogMaxCount, CharNgramModel? Ngrams,
    Script Script, bool CommaBelow)
{
    public string Key(string word)
    {
        var key = LookupKeys.For(word, Script);
        return CommaBelow ? LookupKeys.FoldCedilla(key) : key;
    }

    // Ukrainian apostrophe words: known when every part between marks is; rarest part sets the count.
    public long? CountOf(string word)
    {
        if (Counts.TryGetValue(Key(word), out var known))
            return known;

        if (Script != Script.Cyrillic || !LookupKeys.HasInternalMark(word, Script))
            return null;

        long? rarest = null;
        foreach (var part in Scripts.FoldMarks(word.ToLowerInvariant(), Script).Split('\''))
        {
            if (part.Length == 0)
                return null;

            if (!Counts.TryGetValue(Key(part), out var partCount))
                return null;

            if (rarest is null || partCount < rarest)
                rarest = partCount;
        }

        return rarest;
    }

    public double Normalise(long count)
    {
        return LogMaxCount <= 0.0 ? 0.0 : Math.Clamp(Math.Log(1 + count) / LogMaxCount, 0.0, 1.0);
    }
}
