using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Sweep;

internal readonly record struct FrequencyWord(string Word, long Count);

internal readonly record struct LoadStats(int Kept, int Folded, int Merged, int Dropped, int Untypeable)
{
    public override string ToString()
    {
        return $"{Kept} words (folded {Folded} marked entries, merged {Merged}, dropped {Dropped} not of the script and {Untypeable} the layout cannot type)";
    }
}

internal sealed class FrequencyList
{
    private readonly List<FrequencyWord> _words;
    private readonly long[] _cumulative;

    private FrequencyList(List<FrequencyWord> words)
    {
        _words = words;
        _cumulative = new long[words.Count];

        long running = 0;
        for (var i = 0; i < words.Count; i++)
        {
            running += words[i].Count;
            _cumulative[i] = running;
        }

        Total = running;
        MaxCount = words.Count == 0 ? 0 : words.Max(w => w.Count);
    }

    public int Count => _words.Count;

    public long Total { get; }

    public long MaxCount { get; }

    public IReadOnlyList<FrequencyWord> Words => _words;

    public static FrequencyList FromWords(List<FrequencyWord> words)
    {
        return new FrequencyList(words);
    }

    public static FrequencyList Load(string path)
    {
        return Load(path, null, out _);
    }

    public static FrequencyList Load(string path, TargetLanguage? target, out LoadStats stats)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, long>();
        var folded = 0;
        var merged = 0;
        var dropped = 0;
        var untypeable = 0;

        foreach (var line in File.ReadLines(path))
        {
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !long.TryParse(fields[1], out var count) || count <= 0)
                continue;

            var word = fields[0];
            if (target is not null)
            {
                word = Scripts.FoldMarks(word, target.Script);
                if (!ReferenceEquals(word, fields[0]))
                    folded++;

                if (!Scripts.IsWordOf(word, target.Script) ||
                    !LanguageAlphabets.IsWordOf(word, target.Script, target.Code) ||
                    NeighbourWords.IsListed(target.Code, word.ToLowerInvariant()))
                {
                    dropped++;
                    continue;
                }

                if (!target.CanType(word))
                {
                    untypeable++;
                    continue;
                }
            }

            if (counts.TryAdd(word, count))
                order.Add(word);
            else
            {
                counts[word] += count;
                merged++;
            }
        }

        stats = new LoadStats(order.Count, folded, merged, dropped, untypeable);
        return new FrequencyList(order.Select(w => new FrequencyWord(w, counts[w])).ToList());
    }

    public string Sample(Random random)
    {
        var pick = (long)(random.NextDouble() * Total);
        var lo = 0;
        var hi = _cumulative.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_cumulative[mid] <= pick)
                lo = mid + 1;
            else
                hi = mid;
        }

        return _words[lo].Word;
    }

    public string[] SampleLine(Random random, int minWords, int maxWords)
    {
        var count = random.Next(minWords, maxWords + 1);
        var line = new string[count];
        for (var i = 0; i < count; i++)
            line[i] = Sample(random);

        return line;
    }

    public (List<FrequencyWord> Kept, List<FrequencyWord> HeldOut) HoldOutEveryNth(int n)
    {
        var kept = new List<FrequencyWord>(_words.Count);
        var heldOut = new List<FrequencyWord>();

        for (var i = 0; i < _words.Count; i++)
        {
            // Skip the top 500 so OOV samples are rare words.
            if (i > 500 && i % n == 0)
                heldOut.Add(_words[i]);
            else
                kept.Add(_words[i]);
        }

        return (kept, heldOut);
    }
}
