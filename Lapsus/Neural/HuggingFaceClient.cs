using Lapsus.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Neural;

public sealed record HuggingFaceModelSummary(
    string Repo,
    long Downloads,
    string? PipelineTag,
    bool DeclaresGenAi);

public sealed class HuggingFaceClient
{
    private const string Api = "https://huggingface.co/api/models";
    private const string Files = "https://huggingface.co";

    private static readonly string[] GenAiTags = ["onnxruntime-genai", "onnxruntime_genai"];

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(60),
        DefaultRequestHeaders = { { "User-Agent", "Lapsus/1.0" } }
    };

    public static HuggingFaceClient Shared { get; } = new();

    public async Task<IReadOnlyList<RepoFile>> ListFilesAsync(string repo, CancellationToken ct = default)
    {
        var url = $"{Api}/{repo}/tree/main?recursive=1";
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var files = new List<RepoFile>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            if (!entry.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), "file", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!entry.TryGetProperty("path", out var path) || path.GetString() is not { } name)
                continue;

            var size = entry.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0L;
            files.Add(new RepoFile(name, size));
        }

        return files;
    }

    public async Task<IReadOnlyList<HuggingFaceModelSummary>> SearchAsync(
        string? query, int limit = 40, CancellationToken ct = default)
    {
        var found = new Dictionary<string, HuggingFaceModelSummary>(StringComparer.OrdinalIgnoreCase);
        var term = string.IsNullOrWhiteSpace(query) ? "" : $"&search={Uri.EscapeDataString(query.Trim())}";

        var queries = GenAiTags
            .Select(tag => (Url: $"{Api}?filter={tag}&sort=downloads&direction=-1&limit={limit}{term}",
                Tagged: true))
            .Append(($"{Api}?filter=onnx&sort=downloads&direction=-1&limit={limit}{term}", false));

        foreach (var (url, tagged) in queries)
        {
            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("id", out var id)
                    || id.GetString() is not { Length: > 0 } repo)
                    continue;

                var downloads = entry.TryGetProperty("downloads", out var d) && d.TryGetInt64(out var n) ? n : 0L;
                var pipeline = entry.TryGetProperty("pipeline_tag", out var p) ? p.GetString() : null;
                if (tagged || !found.ContainsKey(repo))
                    found[repo] = new HuggingFaceModelSummary(repo, downloads, pipeline, tagged);
            }
        }

        return
        [
            .. found.Values
                .OrderByDescending(m => m.DeclaresGenAi)
                .ThenByDescending(m => m.Downloads)
                .ThenBy(m => m.Repo, StringComparer.Ordinal)
        ];
    }

    public async Task DownloadFilesAsync(
        string repo,
        string subPath,
        IReadOnlyList<RepoFile> files,
        string destination,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var totalBytes = files.Sum(f => f.Size);
        long downloaded = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var relative = RelativePath(subPath, file.Path);

            if (string.IsNullOrEmpty(relative) || relative.Contains("..", StringComparison.Ordinal))
                continue;

            var target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using var response = await Http.GetStreamAsync(FileUrl(repo, file.Path), ct)
                .ConfigureAwait(false);
            await using var output = File.Create(target);

            var buffer = new byte[81920];
            int read;
            while ((read = await response.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                downloaded += read;
                if (totalBytes > 0)
                    progress?.Report(Math.Clamp(downloaded / (double)totalBytes, 0, 1));
            }
        }
    }

    private static string RelativePath(string subPath, string fullPath)
    {
        return subPath.Length > 0 && fullPath.StartsWith(subPath + "/", StringComparison.Ordinal)
            ? fullPath[(subPath.Length + 1)..]
            : fullPath;
    }

    private static string FileUrl(string repo, string path)
    {
        return $"{Files}/{repo}/resolve/main/{path}";
    }

    private static async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            await using var stream = await Http.GetStreamAsync(url, ct).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null;
        }
    }
}
