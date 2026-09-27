namespace Lapsus.Core.Models;

public readonly record struct RepoFile(string Path, long Size);

public static class ModelPackScanner
{
    public static IReadOnlyList<ModelVariant> Scan(IEnumerable<RepoFile> files)
    {
        var listing = files as IReadOnlyList<RepoFile> ?? [.. files];

        return
        [
            .. PackFolders(listing)
                .Select(folder => new ModelVariant(
                    folder,
                    GenAiPack.InferDevice(folder),
                    GenAiPack.InferProvider(folder),
                    (int)(FilesOf(listing, folder).Sum(f => f.Size) / (1024 * 1024))))
                .OrderBy(v => v.SizeMb)
        ];
    }

    public static IReadOnlyList<RepoFile> FilesOf(IReadOnlyList<RepoFile> files, string subPath)
    {
        var nested = PackFolders(files)
            .Where(f => !f.Equals(subPath, StringComparison.Ordinal) && IsUnder(subPath, f))
            .ToList();

        return
        [
            .. files.Where(f => IsUnder(subPath, f.Path))
                .Where(f => !nested.Any(n => IsUnder(n, f.Path)))
                .Where(f => !FileName(f.Path).StartsWith(".git", StringComparison.OrdinalIgnoreCase))
        ];
    }

    private static IEnumerable<string> PackFolders(IEnumerable<RepoFile> files)
    {
        return files.Where(f => FileName(f.Path).Equals(GenAiPack.ConfigFile, StringComparison.OrdinalIgnoreCase))
            .Select(f => FolderOf(f.Path))
            .Distinct(StringComparer.Ordinal);
    }

    private static bool IsUnder(string folder, string path)
    {
        return folder.Length == 0 || path.StartsWith(folder + "/", StringComparison.Ordinal);
    }

    private static string FolderOf(string path)
    {
        var cut = path.LastIndexOf('/');
        return cut < 0 ? "" : path[..cut];
    }

    private static string FileName(string path)
    {
        var cut = path.LastIndexOf('/');
        return cut < 0 ? path : path[(cut + 1)..];
    }
}
