using Lapsus.Core.Models;
using Lapsus.Neural;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Speech;

// Under %APPDATA%/Lapsus/speech, outside the install folder updates replace.
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

    // The model's folder: Parakeet reads its files from there, Whisper reads PathFor.
    public string FolderFor(SpeechModel model) => Path.Combine(Directory, model.Id);

    public string PathFor(SpeechModel model) => Path.Combine(FolderFor(model), model.Files[0].Path);

    public string VadPath => Path.Combine(Directory, "vad", SpeechModelCatalog.Vad.Path);

    public bool IsInstalled(SpeechModel model) =>
        model.Files.All(f => IsComplete(Path.Combine(FolderFor(model), f.Path), f))
        && (!NeedsVad(model) || IsComplete(VadPath, SpeechModelCatalog.Vad));

    public async Task DownloadAsync(SpeechModel model, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var files = model.Files.Select(f => (File: f, Target: Path.Combine(FolderFor(model), f.Path))).ToList();
        if (NeedsVad(model))
            files.Insert(0, (SpeechModelCatalog.Vad, VadPath));

        var total = (double)files.Sum(f => f.File.Size);
        long done = 0;
        foreach (var (file, target) in files)
        {
            var before = done;
            if (!IsComplete(target, file))
                await DownloadFileAsync(file, target,
                    new Progress<double>(p => progress?.Report((before + p * file.Size) / total)), ct)
                    .ConfigureAwait(false);
            done += file.Size;
        }

        progress?.Report(1);
    }

    public void Remove(SpeechModel model)
    {
        var dir = FolderFor(model);
        if (System.IO.Directory.Exists(dir))
            System.IO.Directory.Delete(dir, true);
    }

    // Whisper writes subtitle credits over silence without Silero cutting it away; Parakeet writes nothing.
    private static bool NeedsVad(SpeechModel model) => model.Engine == SpeechEngine.Whisper;

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
