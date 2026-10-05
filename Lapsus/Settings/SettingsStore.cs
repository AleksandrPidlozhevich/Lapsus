using Lapsus.Core.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lapsus.Settings;

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lapsus");
        _path = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
                       ?? new AppSettings();
            }
        }
        catch
        {

        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
            File.WriteAllText(_path, json);
        }
        catch
        {
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ModelManifest))]
internal partial class SettingsJsonContext : JsonSerializerContext;
