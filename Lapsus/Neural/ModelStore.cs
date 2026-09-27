using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lapsus.Core.Models;
using Lapsus.Localization;
using Lapsus.Settings;

namespace Lapsus.Neural;

// Models under %APPDATA%/Lapsus — not next to the exe (Velopack wipes the install folder).
public sealed class ModelStore
{
    private const string ManifestFile = "lapsus_model.json";

    private readonly HuggingFaceClient _client;

    public ModelStore(HuggingFaceClient? client = null)
    {
        _client = client ?? HuggingFaceClient.Shared;
        Directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus", "models");
    }

    public string Directory { get; }

    public string PathFor(string id)
    {
        return Path.Combine(Directory, id);
    }

    public bool IsInstalled(string id)
    {
        var dir = PathFor(id);

        return File.Exists(Path.Combine(dir, GenAiPack.ConfigFile))
               && System.IO.Directory.EnumerateFiles(dir, "*.onnx", SearchOption.AllDirectories).Any();
    }

    public IReadOnlyList<ModelDescriptor> Installed()
    {
        var list = new List<ModelDescriptor>();
        if (!System.IO.Directory.Exists(Directory))
            return list;

        foreach (var dir in System.IO.Directory.EnumerateDirectories(Directory))
        {
            var id = Path.GetFileName(dir);

            if (id.EndsWith(".download", StringComparison.Ordinal) || !IsInstalled(id))
                continue;

            list.Add(Describe(id) ?? new ModelDescriptor(
                id, id, "", [new ModelVariant("", ComputeDevice.Cpu, "", EstimateRamMb(dir))], ""));
        }

        return list;
    }

    public ModelDescriptor? Describe(string id)
    {
        return ReadManifest(PathFor(id))?.ToDescriptor() ?? ModelCatalog.Find(id);
    }

    public ModelVariant InstalledVariant(string id)
    {
        return ReadManifest(PathFor(id))?.ToVariant()
               ?? new ModelVariant("", GenAiPack.InferDevice("", DeclaredProviders(id)));
    }

    public IReadOnlyList<string> DeclaredProviders(string id)
    {
        try
        {
            var path = Path.Combine(PathFor(id), GenAiPack.ConfigFile);
            return File.Exists(path) ? GenAiPack.ReadProviders(File.ReadAllText(path)) : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public async Task DownloadAsync(
        ModelDescriptor descriptor,
        ModelVariant variant,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var staging = PathFor(descriptor.Id) + ".download";
        if (System.IO.Directory.Exists(staging))
            System.IO.Directory.Delete(staging, true);
        System.IO.Directory.CreateDirectory(staging);

        try
        {
            var listing = await _client.ListFilesAsync(descriptor.HuggingFaceRepo, ct).ConfigureAwait(false);
            var files = ModelPackScanner.FilesOf(listing, variant.SubPath);
            if (files.Count == 0)
                throw new InvalidOperationException(Localizer.Instance["Model_DownloadEmpty"]);

            await _client.DownloadFilesAsync(
                    descriptor.HuggingFaceRepo, variant.SubPath, files, staging, progress, ct)
                .ConfigureAwait(false);

            if (!File.Exists(Path.Combine(staging, GenAiPack.ConfigFile))
                || !System.IO.Directory.EnumerateFiles(staging, "*.onnx", SearchOption.AllDirectories).Any())
                throw new InvalidOperationException(Localizer.Instance["Model_DownloadEmpty"]);

            var final = PathFor(descriptor.Id);
            if (System.IO.Directory.Exists(final))
                System.IO.Directory.Delete(final, true);
            System.IO.Directory.Move(staging, final);

            WriteManifest(final, descriptor, variant);
            progress?.Report(1);
        }
        catch
        {
            if (System.IO.Directory.Exists(staging))
                System.IO.Directory.Delete(staging, true);
            throw;
        }
    }

    public void Remove(string id)
    {
        var path = PathFor(id);
        if (System.IO.Directory.Exists(path))
            System.IO.Directory.Delete(path, true);
    }

    private static void WriteManifest(string dir, ModelDescriptor descriptor, ModelVariant variant)
    {
        var declared = ReadDeclaredProviders(dir);
        var provider = declared.FirstOrDefault(p => ExecutionProviders.DeviceOf(p) != ComputeDevice.Cpu)
                       ?? variant.Provider;
        var device = provider.Length > 0 ? ExecutionProviders.DeviceOf(provider) : variant.Device;

        var ramMb = variant.SizeMb > 0 ? variant.SizeMb : EstimateRamMb(dir);

        try
        {
            var manifest = new ModelManifest(
                descriptor.Id, descriptor.DisplayName, descriptor.HuggingFaceRepo,
                variant.SubPath, ramMb, descriptor.License, ModelCatalog.IsCustom(descriptor.Id))
            {
                Device = device,
                Provider = provider
            };

            var json = JsonSerializer.Serialize(manifest, SettingsJsonContext.Default.ModelManifest);
            File.WriteAllText(Path.Combine(dir, ManifestFile), json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private static IReadOnlyList<string> ReadDeclaredProviders(string dir)
    {
        try
        {
            var path = Path.Combine(dir, GenAiPack.ConfigFile);
            return File.Exists(path) ? GenAiPack.ReadProviders(File.ReadAllText(path)) : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static ModelManifest? ReadManifest(string dir)
    {
        try
        {
            var path = Path.Combine(dir, ManifestFile);
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize(
                File.ReadAllText(path), SettingsJsonContext.Default.ModelManifest);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static int EstimateRamMb(string dir)
    {
        try
        {
            long bytes = 0;
            foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(file).Length;
            return Math.Max((int)(bytes / (1024 * 1024)), 50);
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
