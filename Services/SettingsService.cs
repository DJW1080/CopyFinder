using System.Text.Json;
using CopyFinder.Models;

namespace CopyFinder.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopyFinder");

        var directoryResult = SafeFile.EnsureDirectory(settingsDirectory);
        if (!directoryResult.Succeeded)
        {
            DeploymentLogger.Log("Settings", $"Could not prepare settings directory: {directoryResult.Message}");
        }

        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var result = SafeFile.WriteAllText(_settingsPath, json);
        if (!result.Succeeded)
        {
            throw new IOException(result.Message);
        }
    }
}
