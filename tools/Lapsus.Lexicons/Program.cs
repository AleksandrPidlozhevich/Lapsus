using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

string? Option(string name)
{
    var at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

var code = Option("--lang");
var outDir = Option("--out");

if (Option("--russian-in") is { } oldList)
{
    var reference = Option("--reference");
    var ownPath = Option("--own");
    var neighbourPath = Option("--neighbour");
    if (code is null || outDir is null || reference is null || ownPath is null || neighbourPath is null)
    {
        Console.Error.WriteLine("Usage: --lang xx --russian-in FILE --reference FILE --own FILE.dic --neighbour FILE.dic [--ratio 5] --out FILE");
        return 1;
    }

    var ratioMin = double.Parse(Option("--ratio") ?? "5", System.Globalization.CultureInfo.InvariantCulture);
    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    var ownWords = WeCantSpell.Hunspell.WordList.CreateFromFiles(ownPath, Path.ChangeExtension(ownPath, ".aff"));
    var neighbourWords = WeCantSpell.Hunspell.WordList.CreateFromFiles(neighbourPath, Path.ChangeExtension(neighbourPath, ".aff"));

    Dictionary<string, long> Read(string path)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && long.TryParse(fields[^1], out var count))
            {
                var key = fields[0].ToLowerInvariant();
                counts[key] = counts.GetValueOrDefault(key) + count;
            }
        }

        return counts;
    }

    var before = Read(oldList);
    var after = Read(reference);
    double beforeTotal = before.Values.Sum(), afterTotal = after.Values.Sum();

    string[] apostrophePrefixes = ["б", "в", "м", "п", "ф", "р", "з", "об", "від", "під", "роз", "без", "пів", "над", "між"];
    var found = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var (word, count) in before)
    {
        // ≥20 sightings; unseen in reference counts as once; skip apostrophe-word halves.
        if (count < 20 || !word.All(char.IsLetter) || !neighbourWords.Check(word))
            continue;

        var ratio = count / beforeTotal / ((after.GetValueOrDefault(word) + 1) / afterTotal);
        if (ratio >= ratioMin && !apostrophePrefixes.Any(p => ownWords.Check($"{p}'{word}")))
            found.Add(word);
    }

    File.WriteAllLines(outDir, found, new System.Text.UTF8Encoding(false));
    Console.WriteLine($"{code}: {found.Count} of the neighbour's words at ×{ratioMin} → {outDir} (add the header back)");
    return 0;
}

if (Option("--neighbour") is { } neighbourDic)
{
    var list = Option("--list");
    var ownDic = Option("--own");
    if (code is null || outDir is null || list is null || ownDic is null)
    {
        Console.Error.WriteLine("Usage: --lang xx --list FILE --own FILE.dic --neighbour FILE.dic --out DIR");
        return 1;
    }

    // Older dictionaries name a code page (el_GR is ISO 8859-7); SpellChecker registers the same provider.
    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    var own = WeCantSpell.Hunspell.WordList.CreateFromFiles(ownDic, Path.ChangeExtension(ownDic, ".aff"));
    var theirs = WeCantSpell.Hunspell.WordList.CreateFromFiles(neighbourDic, Path.ChangeExtension(neighbourDic, ".aff"));
    var kept = NeighbourWords.Remove(File.ReadLines(list), own, theirs, out var removed);
    Directory.CreateDirectory(outDir);
    File.WriteAllLines(Path.Combine(outDir, $"{code}.txt"), kept, new System.Text.UTF8Encoding(false));
    Console.WriteLine($"{code}: {removed} of the neighbour's words taken out, {kept.Count} lines kept → {outDir}");
    return 0;
}
var kaikki = Option("--kaikki");
var unimorph = args.Select((a, i) => (a, i)).Where(x => x.a == "--unimorph" && x.i + 1 < args.Length)
    .Select(x => args[x.i + 1]).ToList();
if (code is null || outDir is null || (kaikki is null && unimorph.Count == 0))
{
    Console.Error.WriteLine("Usage: --lang xx --out DIR [--kaikki FILE.jsonl] [--unimorph FILE]...");
    return 1;
}

var script = code switch
{
    "he" => Script.Hebrew,
    "ar" => Script.Arabic,
    "el" => Script.Greek,
    "ka" => Script.Georgian,
    "uk" or "ru" or "be" or "bg" or "mk" => Script.Cyrillic,
    _ => Script.Latin
};

var builder = new WordFormsBuilder(code, script);
if (kaikki is not null)
    Console.WriteLine($"Wiktionary (kaikki.org): {builder.AddWiktionary(File.ReadLines(kaikki))} forms");
foreach (var file in unimorph)
    Console.WriteLine($"UniMorph ({Path.GetFileName(file)}): {builder.AddUniMorph(File.ReadLines(file))} forms not already in");

Directory.CreateDirectory(outDir);
builder.Write(Path.Combine(outDir, $"{code}.dic"));
Console.WriteLine($"{code}: {builder.Count} forms → {outDir}");
return 0;
