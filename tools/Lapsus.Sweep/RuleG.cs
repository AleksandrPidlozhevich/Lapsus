using Lapsus.Core.Layout;

namespace Lapsus.Sweep;

internal static class RuleG
{
    private static readonly string[] Kinds = ["neighbour", "swapped", "dropped", "extra", "doubled"];

    private static readonly string[] Lengths = ["4", "5-6", "7-9", "10+"];

    // One slip of the fingers in a word typed on its own layout, as Space hands it over: is the meant word
    // what comes back? Every kind of slip is tried on every word, so a fix that helps one kind and hurts
    // another shows up as such.
    public static void Typos(Machine machine, FrequencyList target, int lines)
    {
        Report.Heading($"Rule G — a {machine.Target.Name} word with one typo, on its own");

        var random = new Random(20261003);
        var words = new List<string>();
        for (var tries = 0; words.Count < Math.Max(40, lines / 50) && tries < lines * 10; tries++)
        {
            var word = target.Sample(random);
            if (word.Length >= 4 && word.All(char.IsLetter) && machine.Target.CanType(word))
                words.Add(word);
        }

        var tally = new Tally();
        foreach (var word in words)
            foreach (var kind in Kinds)
                if (Mistype(word, kind, machine.Target.Map, random) is { } typo && !machine.IsAnyWord(typo))
                    tally.Add(machine, word, typo, kind, machine.Correct(typo, true, machine.TargetSource).Corrected);

        tally.Report();

        // The everyday sentences carry the inflected forms a frequency list is short of.
        var sentences = NeuralRun.Sentences(machine.Target.Code);
        if (sentences.Count == 0)
            return;

        Report.Heading($"Rule G — a typo inside a natural {machine.Target.Name} sentence");

        var inLine = new Tally();
        foreach (var sentence in sentences)
        {
            var parts = Machine.Words(sentence);
            for (var at = 0; at < parts.Length; at++)
            {
                if (parts[at].Length < 4 || !parts[at].All(char.IsLetter))
                    continue;

                foreach (var kind in Kinds)
                {
                    if (Mistype(parts[at], kind, machine.Target.Map, random) is not { } typo || machine.IsAnyWord(typo))
                        continue;

                    var copy = (string[])parts.Clone();
                    copy[at] = typo;
                    var corrected = Machine.Words(machine.Correct(string.Join(' ', copy), true, machine.TargetSource).Corrected);
                    inLine.Add(machine, parts[at], typo, kind, corrected.Length == parts.Length ? corrected[at] : string.Join(' ', corrected));
                }
            }
        }

        inLine.Report();
    }

    private static string? Mistype(string word, string kind, KeyboardMap map, Random random)
    {
        var pos = random.Next(1, word.Length - 1);
        var near = KeyNeighbours.Of(word[pos], map);
        return kind switch
        {
            "neighbour" when near.Count > 0 => word[..pos] + near[random.Next(near.Count)] + word[(pos + 1)..],
            "swapped" => word[..pos] + word[pos + 1] + word[pos] + word[(pos + 2)..],
            "dropped" => word.Remove(pos, 1),
            "extra" when near.Count > 0 => word.Insert(pos, near[random.Next(near.Count)].ToString()),
            "doubled" => word.Insert(pos, word[pos].ToString()),
            _ => null
        };
    }

    private sealed class Tally
    {
        private readonly Dictionary<string, (int Fixed, int Total)> _byKind = new();
        private readonly Dictionary<string, (int Fixed, int Total)> _byLength = new();
        private readonly List<string> _examples = [];
        private int _total;
        private int _fixed;
        private int _kept;
        private int _crossed;

        public void Add(Machine machine, string meant, string typo, string kind, string got)
        {
            _total++;
            var length = meant.Length <= 4 ? "4" : meant.Length <= 6 ? "5-6" : meant.Length <= 9 ? "7-9" : "10+";
            var right = got == meant;

            Count(_byKind, kind, right);
            Count(_byLength, length, right);

            if (right)
            {
                _fixed++;
                return;
            }

            var crossed = Scripts.Dominant(got) != machine.Target.Script;
            if (got == typo)
                _kept++;
            else if (crossed)
                _crossed++;

            if (_examples.Count < 8 && got != typo)
                _examples.Add($"{typo} → {got}   (wanted {meant}, {kind})");
        }

        public void Report()
        {
            if (_total == 0)
                return;

            Sweep.Report.Value("fixed back to the meant word", Rate(_fixed, _total), Better.Higher);
            Sweep.Report.Value("changed into another word", Rate(_total - _fixed - _kept - _crossed, _total), Better.Lower);
            Sweep.Report.Value("crossed into another layout", Rate(_crossed, _total), Better.Lower);
            Sweep.Report.Value("left as typed", Rate(_kept, _total), Better.Either);
            Sweep.Report.Cells("fixed, by slip", Kinds.Where(_byKind.ContainsKey).ToList(),
                Kinds.Where(_byKind.ContainsKey).Select(k => Rate(_byKind[k].Fixed, _byKind[k].Total)).ToList(), Better.Higher);
            Sweep.Report.Cells("fixed, by word length", Lengths.Where(_byLength.ContainsKey).ToList(),
                Lengths.Where(_byLength.ContainsKey).Select(l => Rate(_byLength[l].Fixed, _byLength[l].Total)).ToList(), Better.Higher);
            Sweep.Report.Line("typos tried", _total.ToString());

            foreach (var example in _examples)
                Sweep.Report.Line(string.Empty, example);
        }

        private static void Count(Dictionary<string, (int Fixed, int Total)> into, string key, bool right)
        {
            var (fixedCount, total) = into.GetValueOrDefault(key);
            into[key] = (fixedCount + (right ? 1 : 0), total + 1);
        }

        private static double Rate(int part, int whole)
        {
            return (double)part / whole;
        }
    }
}
