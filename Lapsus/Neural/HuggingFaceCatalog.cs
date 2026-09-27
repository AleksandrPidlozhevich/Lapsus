using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lapsus.Core.Models;

namespace Lapsus.Neural;

public sealed class HuggingFaceCatalog
{

    private const int Candidates = 12;

    private const int Parallelism = 4;

    private readonly HuggingFaceClient _client;

    public HuggingFaceCatalog(HuggingFaceClient? client = null)
    {
        _client = client ?? HuggingFaceClient.Shared;
    }

    public async Task<ModelDescriptor?> DescribeAsync(
        HuggingFaceModelRef reference, CancellationToken ct = default)
    {
        var files = await _client.ListFilesAsync(reference.Repo, ct).ConfigureAwait(false);
        if (files.Count == 0)
            return null;

        var packs = ModelPackScanner.Scan(files);
        if (packs.Count == 0)
            return null;

        if (reference.SubPath.Length > 0)
        {
            var exact = packs.FirstOrDefault(p =>
                p.SubPath.Equals(reference.SubPath, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                packs = [exact];
        }

        return ModelCatalog.CreateCustom(reference.Repo, reference.SubPath, packs);
    }

    public async Task<IReadOnlyList<ModelDescriptor>> SearchAsync(
        string query, CancellationToken ct = default)
    {
        var repos = await _client.SearchAsync(query, ct: ct).ConfigureAwait(false);
        var wanted = repos
            .Where(r => IsTextModel(r.PipelineTag))
            .Where(r => ModelCatalog.Available.All(m =>
                !m.HuggingFaceRepo.Equals(r.Repo, StringComparison.OrdinalIgnoreCase)))
            .Take(Candidates)
            .ToList();

        var found = new List<ModelDescriptor>();
        foreach (var chunk in wanted.Chunk(Parallelism))
        {
            ct.ThrowIfCancellationRequested();
            var described = await Task
                .WhenAll(chunk.Select(r => DescribeAsync(new HuggingFaceModelRef(r.Repo, ""), ct)))
                .ConfigureAwait(false);
            found.AddRange(described.OfType<ModelDescriptor>());
        }

        return [.. found.OrderBy(m => m.RamMb)];
    }

    private static bool IsTextModel(string? pipelineTag)
    {
        return pipelineTag is null or ""
               || pipelineTag is "text-generation" or "text2text-generation" or "image-text-to-text";
    }
}
