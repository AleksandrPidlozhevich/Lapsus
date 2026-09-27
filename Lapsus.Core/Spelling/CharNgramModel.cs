using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Spelling;

public sealed class CharNgramModel
{
    // Below this a list teaches nothing reliable; use the vowel heuristic instead.
    public const int MinTrainingWords = 200;

    private const char Start = '';
    private const char End = '';

    private const double Smoothing = 0.5;

    private readonly Dictionary<long, double> _trigrams;
    private readonly Dictionary<int, double> _contexts;
    private readonly int _alphabet;

    private readonly double _hi;
    private readonly double _lo;

    private CharNgramModel(
        Dictionary<long, double> trigrams, Dictionary<int, double> contexts, int alphabet, double hi, double lo,
        int trainedWords)
    {
        _trigrams = trigrams;
        _contexts = contexts;
        _alphabet = alphabet;
        _hi = hi;
        _lo = lo;
        TrainedWords = trainedWords;
    }

    public int TrainedWords { get; }

    public static CharNgramModel? Build(
        IEnumerable<(string Word, long Count)> words, Script script, int minWords = MinTrainingWords)
    {
        var trigrams = new Dictionary<long, double>();
        var contexts = new Dictionary<int, double>();
        var letters = new HashSet<char>();
        var trained = new List<(string Word, double Weight)>();

        foreach (var (raw, count) in words)
        {
            if (count <= 0 || string.IsNullOrEmpty(raw))
                continue;

            var word = raw.ToLowerInvariant();
            if (!WordScanner.IsAllLetters(word, script))
                continue;

            var weight = Math.Log(1 + count);
            trained.Add((word, weight));
            foreach (var ch in word)
                letters.Add(ch);

            var c1 = Start;
            var c2 = Start;
            foreach (var c3 in Padded(word))
            {
                Add(trigrams, Key(c1, c2, c3), weight);
                Add(contexts, Key(c1, c2), weight);
                c1 = c2;
                c2 = c3;
            }
        }

        if (trained.Count < minWords)
            return null;

        var model = new CharNgramModel(trigrams, contexts, letters.Count + 1, 0.0, 0.0, trained.Count);

        var real = new List<(double Value, double Weight)>(trained.Count);
        var salad = new List<(double Value, double Weight)>(trained.Count);
        foreach (var (word, weight) in trained)
        {
            real.Add((model.AverageLogProb(word), weight));
            salad.Add((model.AverageLogProb(Reverse(word)), weight));
        }

        var hi = WeightedMedian(real);
        var lo = WeightedMedian(salad);
        if (hi - lo < 0.1)
            return null;

        return new CharNgramModel(trigrams, contexts, model._alphabet, hi, lo, trained.Count);
    }

    public double Score(string word)
    {
        if (string.IsNullOrEmpty(word))
            return 0.0;

        var lower = word.ToLowerInvariant();
        return Math.Clamp((AverageLogProb(lower) - _lo) / (_hi - _lo), 0.0, 1.0);
    }

    public double AverageLogProb(string word)
    {
        if (string.IsNullOrEmpty(word))
            return double.NegativeInfinity;

        var sum = 0.0;
        var n = 0;
        var c1 = Start;
        var c2 = Start;
        foreach (var c3 in Padded(word))
        {
            _trigrams.TryGetValue(Key(c1, c2, c3), out var seen);
            _contexts.TryGetValue(Key(c1, c2), out var context);
            sum += Math.Log10((seen + Smoothing) / (context + Smoothing * _alphabet));
            n++;
            c1 = c2;
            c2 = c3;
        }

        return sum / n;
    }

    private static IEnumerable<char> Padded(string word)
    {
        foreach (var ch in word)
            yield return ch;

        yield return End;
    }

    private static void Add(Dictionary<long, double> counts, long key, double weight)
    {
        counts.TryGetValue(key, out var seen);
        counts[key] = seen + weight;
    }

    private static void Add(Dictionary<int, double> counts, int key, double weight)
    {
        counts.TryGetValue(key, out var seen);
        counts[key] = seen + weight;
    }

    private static long Key(char c1, char c2, char c3)
    {
        return ((long)c1 << 32) | ((long)c2 << 16) | c3;
    }

    private static int Key(char c1, char c2)
    {
        return (c1 << 16) | c2;
    }

    private static string Reverse(string word)
    {
        var chars = word.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    private static double WeightedMedian(List<(double Value, double Weight)> samples)
    {
        samples.Sort((a, b) => a.Value.CompareTo(b.Value));
        var total = 0.0;
        foreach (var (_, weight) in samples)
            total += weight;

        var seen = 0.0;
        foreach (var (value, weight) in samples)
        {
            seen += weight;
            if (seen >= total / 2)
                return value;
        }

        return samples[^1].Value;
    }
}
