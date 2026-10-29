using Lapsus.Core.Models;
using Lapsus.Neural;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Speech;

// Speech models under %APPDATA%/Lapsus/speech (next to models/, outside the install folder updates replace):
// speech/<model id>/<file> and speech/vad/<file>. A file counts as installed at its catalog size; a download
// lands in a .download file, is checked against its SHA-256 and only then takes the real name.
public sealed class SpeechModelStore
{
    private readonly HuggingFaceClient _client;

    public SpeechModelStore(HuggingFaceClient? client = null, string? directory = null)
    {
        _client = client ?? HuggingFaceClient.Shared;
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus", "speech");
    }

    public string Directory { get; }

    public string PathFor(SpeechModel model) => Path.Combine(Directory, model.Id, model.File.Path);

    public string VadPath => Path.Combine(Directory, "vad", SpeechModelCatalog.Vad.Path);

    public bool IsInstalled(SpeechModel model) => IsComplete(PathFor(model), model.File) && IsComplete(VadPath, SpeechModelCatalog.Vad);

    public async Task DownloadAsync(SpeechModel model, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var total = (double)(model.File.Size + SpeechModelCatalog.Vad.Size);
        if (!IsComplete(VadPath, SpeechModelCatalog.Vad))
            await DownloadFileAsync(SpeechModelCatalog.Vad, VadPath,
                new Progress<double>(p => progress?.Report(p * SpeechModelCatalog.Vad.Size / total)), ct)
                .ConfigureAwait(false);

        if (!IsComplete(PathFor(model), model.File))
            await DownloadFileAsync(model.File, PathFor(model),
                new Progress<double>(p => progress?.Report((SpeechModelCatalog.Vad.Size + p * model.File.Size) / total)), ct)
                .ConfigureAwait(false);

        progress?.Report(1);
    }

    public void Remove(SpeechModel model)
    {
        var dir = Path.GetDirectoryName(PathFor(model))!;
        if (System.IO.Directory.Exists(dir))
            System.IO.Directory.Delete(dir, true);
    }

    private async Task DownloadFileAsync(SpeechFile file, string target, IProgress<double> progress, CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(target)!;
        System.IO.Directory.CreateDirectory(folder);
        var staging = Path.Combine(folder, ".download");
        if (System.IO.Directory.Exists(staging))
            System.IO.Directory.Delete(staging, true);

        try
        {
            await _client.DownloadFilesAsync(file.Repo, file.Revision, "", [new RepoFile(file.Path, file.Size)],
                staging, progress, ct).ConfigureAwait(false);

            var downloaded = Path.Combine(staging, file.Path);
            if (!File.Exists(downloaded) || new FileInfo(downloaded).Length != file.Size)
                throw new InvalidDataException($"{file.Path}: incomplete download");
            if (!await HasHashAsync(downloaded, file.Sha256, ct).ConfigureAwait(false))
                throw new InvalidDataException($"{file.Path}: checksum does not match");

            File.Move(downloaded, target, overwrite: true);
        }
        finally
        {
            if (System.IO.Directory.Exists(staging))
                System.IO.Directory.Delete(staging, true);
        }
    }

    private static bool IsComplete(string path, SpeechFile file)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length == file.Size;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task<bool> HasHashAsync(string path, string sha256, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return string.Equals(Convert.ToHexStringLower(hash), sha256, StringComparison.OrdinalIgnoreCase);
    }
}
