using Lapsus.Core.Spelling;
using Lapsus.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WeCantSpell.Hunspell;

namespace Lapsus.Settings;

public sealed class DictionaryStore
{

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Brotli
    })
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public string Directory { get; }

    // Dictionaries under %APPDATA%/Lapsus — not next to the exe (Velopack wipes the install folder).
    public DictionaryStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus", "dictionaries"))
    {
    }

    internal DictionaryStore(string directory)
    {
        Directory = directory;
    }

    public string PathFor(string code)
    {
        return Path.Combine(Directory, code + ".txt");
    }

    private string WordFormsPathFor(string code)
    {
        return Path.Combine(Directory, code + ".dic");
    }

    private string VersionPathFor(string code)
    {
        return Path.Combine(Directory, code + ".version");
    }

    public bool IsInstalled(string code)
    {
        return File.Exists(PathFor(code));
    }

    public bool IsOutdated(DictionaryDescriptor descriptor)
    {
        return IsInstalled(descriptor.Code) && InstalledVersion(descriptor.Code) < descriptor.Version;
    }

    private int InstalledVersion(string code)
    {
        try
        {
            var path = VersionPathFor(code);
            return File.Exists(path) &&
                   int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
                ? version
                : 1;
        }
        catch (IOException)
        {
            return 1;
        }
    }

    public IReadOnlyList<DictionarySource> Installed()
    {
        var sources = new List<DictionarySource>();
        foreach (var descriptor in DictionaryCatalog.Available)
        {
            if (!IsInstalled(descriptor.Code))
                continue;

            var wordForms = WordFormsPathFor(descriptor.Code);
            sources.Add(new DictionarySource(descriptor.Code, PathFor(descriptor.Code), descriptor.Script,
                File.Exists(wordForms) ? wordForms : null));
        }

        return sources;
    }

    public async Task DownloadAsync(DictionaryDescriptor descriptor, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var staging = Path.Combine(Directory, $".download-{descriptor.Code}");
        if (System.IO.Directory.Exists(staging))
            System.IO.Directory.Delete(staging, true);
        System.IO.Directory.CreateDirectory(staging);

        try
        {
            var list = Path.Combine(staging, $"{descriptor.Code}.txt");
            await DownloadFileAsync(descriptor.Url, list, ct);

            if (!SpellChecker.CanRead(list))
                throw new InvalidOperationException(Localizer.Instance["Dict_DownloadEmpty"]);

            string? stagedWords = null;
            if (descriptor.WordForms is { } wordForms)
            {
                stagedWords = Path.Combine(staging, $"{descriptor.Code}.dic");
                await DownloadFileAsync(wordForms.AffixUrl, Path.ChangeExtension(stagedWords, ".aff"), ct);
                await DownloadFileAsync(wordForms.WordsUrl, stagedWords, ct);

                if (descriptor.Neighbour is { } neighbour)
                    await RemoveNeighbourWordsAsync(descriptor, neighbour, list, stagedWords, staging, ct);
            }
            else if (descriptor.FormLists is { } formLists)
            {
                stagedWords = await BuildWordFormsAsync(descriptor, formLists, staging, ct);
            }

            File.Move(list, PathFor(descriptor.Code), true);
            if (stagedWords is not null)
            {
                File.Move(Path.ChangeExtension(stagedWords, ".aff"), Path.ChangeExtension(WordFormsPathFor(descriptor.Code), ".aff"), true);
                File.Move(stagedWords, WordFormsPathFor(descriptor.Code), true);
            }
            else
            {
                DeleteWordForms(descriptor.Code);
            }

            await File.WriteAllTextAsync(VersionPathFor(descriptor.Code),
                descriptor.Version.ToString(CultureInfo.InvariantCulture), ct);
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(staging, true);
            }
            catch (IOException)
            {

            }
        }
    }

    private static async Task RemoveNeighbourWordsAsync(DictionaryDescriptor descriptor, WordFormsSource neighbour,
        string list, string ownWords, string staging, CancellationToken ct)
    {
        var neighbourWords = Path.Combine(staging, "neighbour.dic");
        try
        {
            await DownloadFileAsync(neighbour.AffixUrl, Path.ChangeExtension(neighbourWords, ".aff"), ct);
            await DownloadFileAsync(neighbour.WordsUrl, neighbourWords, ct);
            await Task.Run(() =>
            {
                var own = WordList.CreateFromFiles(ownWords, Path.ChangeExtension(ownWords, ".aff"));
                var theirs = WordList.CreateFromFiles(neighbourWords, Path.ChangeExtension(neighbourWords, ".aff"));
                var kept = NeighbourWords.Remove(File.ReadLines(list), own, theirs, out _);
                File.WriteAllLines(list, kept, new UTF8Encoding(false));
            }, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException
                                       or TaskCanceledException && !ct.IsCancellationRequested)
        {
            CrashLog.Log(ex, $"taking {neighbour.WordsUrl} words out of {descriptor.Code}");
        }
    }

    private static async Task<string?> BuildWordFormsAsync(
        DictionaryDescriptor descriptor, FormListsSource formLists, string staging, CancellationToken ct)
    {
        var builder = new WordFormsBuilder(descriptor.Code, descriptor.Script);
        var sources = new List<(string Url, bool IsWiktionary)>();
        if (formLists.WiktionaryUrl is { } wiktionary)
            sources.Add((wiktionary, true));
        foreach (var url in formLists.UniMorphUrls)
            sources.Add((url, false));

        for (var i = 0; i < sources.Count; i++)
        {
            var file = Path.Combine(staging, $"forms{i}.txt");
            try
            {
                await DownloadFileAsync(sources[i].Url, file, ct);
                var isWiktionary = sources[i].IsWiktionary;
                await Task.Run(() =>
                {
                    if (isWiktionary)
                        builder.AddWiktionary(File.ReadLines(file));
                    else
                        builder.AddUniMorph(File.ReadLines(file));
                }, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException
                                           or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                CrashLog.Log(ex, $"word forms for {descriptor.Code} from {sources[i].Url}");
            }
            finally
            {
                File.Delete(file);
            }
        }

        if (builder.Count == 0)
            return null;

        var words = Path.Combine(staging, $"{descriptor.Code}.dic");
        builder.Write(words);
        return words;
    }

    private static async Task DownloadFileAsync(string url, string path, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(path);
        await source.CopyToAsync(target, ct);

        if (target.Length == 0)
            throw new InvalidOperationException(Localizer.Instance["Dict_DownloadEmpty"]);
    }

    public void Remove(string code)
    {
        foreach (var path in new[] { PathFor(code), VersionPathFor(code) })
            if (File.Exists(path))
                File.Delete(path);

        DeleteWordForms(code);
    }

    private void DeleteWordForms(string code)
    {
        var words = WordFormsPathFor(code);
        foreach (var path in new[] { words, Path.ChangeExtension(words, ".aff") })
            if (File.Exists(path))
                File.Delete(path);
    }
}
