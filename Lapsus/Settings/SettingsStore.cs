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

    // Tests: a file of their own instead of the user's real settings.
    internal SettingsStore(string path)
    {
        _path = path;
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

    // After an import this copy still holds the old settings in memory; one toggle would write them back
    // over the imported file, so nothing more is saved until the app is started again.
    private bool _imported;

    public bool Export(AppSettings settings, string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // False when the file is not an exported settings file; nothing is changed then.
    public bool Import(string path, AppSettings current)
    {
        AppSettings? incoming;
        try
        {
            var json = File.ReadAllText(path);
            // Any JSON object deserializes into defaults; a file of ours always names the main hotkey.
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(nameof(AppSettings.HotkeyVirtualKey), out _))
                return false;

            incoming = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (incoming is null)
            return false;

        // These describe this computer, not the user's choices: the evaluation clock above all.
        incoming.OrganizationDetectedOn = current.OrganizationDetectedOn;
        incoming.OnboardingDone = current.OnboardingDone;
        incoming.AnnouncedUpdateVersion = current.AnnouncedUpdateVersion;
        incoming.MicrophoneName = current.MicrophoneName;
        incoming.SuggestedDictionaries = current.SuggestedDictionaries;

        Save(incoming);
        _imported = true;
        return true;
    }

    public void Save(AppSettings settings)
    {
        if (_imported)
            return;

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
