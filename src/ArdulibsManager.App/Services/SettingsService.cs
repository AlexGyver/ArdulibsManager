using System.Text.Json;
using ArdulibsManager.Models;

namespace ArdulibsManager.Services;

public sealed class SettingsService
{
    private readonly string _appDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArdulibsManager");

    private string SettingsFile => Path.Combine(_appDir, "settings.json");

    public AppSettings Current { get; private set; } = new();

    public async Task<AppSettings> LoadAsync()
    {
        Directory.CreateDirectory(_appDir);
        if (!File.Exists(SettingsFile))
        {
            Current = new AppSettings();
            await SaveAsync(Current);
            return Current;
        }

        var json = await File.ReadAllTextAsync(SettingsFile);
        Current = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
        return Current;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Current = settings;
        Directory.CreateDirectory(_appDir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(SettingsFile, json);
    }
}
