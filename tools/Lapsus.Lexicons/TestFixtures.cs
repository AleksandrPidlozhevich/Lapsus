using Lapsus.Core.Spelling;
using Lapsus.Settings;
using System.Globalization;
using System.Text;
using WeCantSpell.Hunspell;

namespace Lapsus.Lexicons;

// The word lists in Lapsus.Core.Tests/Languages/Fixtures: each catalog language's list taken from its pinned URL
// and installed the way DictionaryStore installs it (Russian words out of Ukrainian, FrequencyListCleaner), then cut
// to its commonest words. words.extra.tsv names rarer words a test needs ("ye", typed for "ну"); they come in at
// the count the full list gives them, and a word the full list lacks stops the run rather than get a made-up one.
internal static class TestFixtures
{
    private const int Top = 1500;

    // English is every other language's partner in the tests, so it gets a longer list.
    private const int EnglishTop = 3000;

    private const string ExtrasFile = "words.extra.tsv";

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    });

    public static async Task<int> BuildAsync(string fixtureDir, string cacheDir, IReadOnlySet<string> only)
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(cacheDir);

        var extras = ReadExtras(Path.Combine(fixtureDir, ExtrasFile));
        var unknown = extras.Keys.Where(code => DictionaryCatalog.Available.All(d => d.Code != code)).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"{ExtrasFile}: no catalog language {string.Join(", ", unknown)}");
            return 1;
        }

        var failed = false;
        foreach (var descriptor in DictionaryCatalog.Available)
        {
            if (only.Count > 0 && !only.Contains(descriptor.Code))
                continue;

            var installed = await InstallAsync(descriptor, cacheDir);
            var full = ReadList(installed);
            var top = descriptor.Code == "en" ? EnglishTop : Top;
            var kept = full.Take(top).ToList();

            var missing = new List<string>();
            foreach (var word in extras.GetValueOrDefault(descriptor.Code, []))
            {
                if (kept.Any(entry => entry.Word == word))
                    continue;

                if (full.FirstOrDefault(entry => entry.Word == word) is { Word: not null } entry)
                    kept.Add(entry);
                else
                    missing.Add(word);
            }

            if (missing.Count > 0)
            {
                Console.Error.WriteLine($"{descriptor.Code}: not in the full list, so not added: {string.Join(" ", missing)}");
                failed = true;
                continue;
            }

            var lines = kept.OrderByDescending(entry => entry.Count).Select(entry => entry.Line);
            var target = Path.Combine(fixtureDir, $"{descriptor.Code}.words.txt");
            await File.WriteAllTextAsync(target, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            Console.WriteLine($"{descriptor.Code}: {kept.Count} words ({kept.Count - Math.Min(top, full.Count)} extra) " +
                              $"of {full.Count} → {target}");
        }

        return failed ? 1 : 0;
    }

    // As DictionaryStore.DownloadAsync, without the Hunspell files the fixtures do not use.
    private static async Task<string> InstallAsync(DictionaryDescriptor descriptor, string cacheDir)
    {
        var list = Path.Combine(cacheDir, $"{descriptor.Code}.installed.txt");
        File.Copy(await FetchAsync(descriptor.Url, cacheDir), list, true);

        if (!SpellChecker.CanRead(list))
            throw new InvalidDataException($"{descriptor.Code}: {descriptor.Url} is not a word list");

        if (descriptor.WordForms is { } wordForms && descriptor.Neighbour is { } neighbour)
        {
            var own = await HunspellAsync(wordForms, cacheDir);
            var theirs = await HunspellAsync(neighbour, cacheDir);
            var kept = NeighbourWords.Remove(File.ReadLines(list), own, theirs, out _);
            await File.WriteAllLinesAsync(list, kept, new UTF8Encoding(false));
        }

        FrequencyListCleaner.CleanFile(list, descriptor.Script);
        return list;
    }

    private static async Task<WordList> HunspellAsync(WordFormsSource source, string cacheDir)
    {
        var affix = await FetchAsync(source.AffixUrl, cacheDir);
        var words = await FetchAsync(source.WordsUrl, cacheDir);
        return WordList.CreateFromFiles(words, affix);
    }

    // Pinned URLs never change, so a download is kept and reused.
    private static async Task<string> FetchAsync(string url, string cacheDir)
    {
        var name = new StringBuilder();
        foreach (var ch in url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..])
            name.Append(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' ? ch : '_');

        var path = Path.Combine(cacheDir, name.ToString());
        if (File.Exists(path))
            return path;

        Console.WriteLine($"  downloading {url}");
        var partial = path + ".part";
        using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(partial);
            await response.Content.CopyToAsync(file);
        }

        File.Move(partial, path, true);
        return path;
    }

    private static List<(string Word, long Count, string Line)> ReadList(string path)
    {
        var entries = new List<(string, long, string)>();
        foreach (var line in File.ReadLines(path))
        {
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && long.TryParse(fields[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                entries.Add((fields[0], count, $"{fields[0]} {count.ToString(CultureInfo.InvariantCulture)}"));
        }

        return entries;
    }

    private static Dictionary<string, List<string>> ReadExtras(string path)
    {
        var extras = new Dictionary<string, List<string>>();
        if (!File.Exists(path))
            return extras;

        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var fields = line.Split('\t');
            if (fields.Length < 2)
                throw new InvalidDataException($"{ExtrasFile}: \"{line}\" needs a language and a word");

            if (!extras.TryGetValue(fields[0], out var words))
                extras[fields[0]] = words = [];
            words.Add(fields[1]);
        }

        return extras;
    }
}
