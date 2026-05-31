using System.Text.Json;
using AutoAuction.Core.Models;

namespace AutoAuction.Core.Services;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> to <c>settings.json</c> in the app root folder.
/// </summary>
public interface ISettingsService
{
    /// <summary>The in-memory settings (loaded on first access / construction).</summary>
    AppSettings Current { get; }

    /// <summary>Writes <see cref="Current"/> back to <c>settings.json</c> and raises <see cref="Changed"/>.</summary>
    void Save();

    /// <summary>Raised after a successful <see cref="Save"/> so dependents (e.g. the bridge server) can react.</summary>
    event Action<AppSettings>? Changed;
}

/// <inheritdoc />
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IAppConfig _config;

    public AppSettings Current { get; private set; }

    public event Action<AppSettings>? Changed;

    public SettingsService(IAppConfig config)
    {
        _config = config;
        Current = Load();
    }

    private AppSettings Load()
    {
        if (File.Exists(_config.SettingsPath))
        {
            try
            {
                var json = File.ReadAllText(_config.SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                    return settings;
            }
            catch (JsonException)
            {
                // Corrupt file - fall through to defaults rather than crashing on startup.
            }
        }

        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(_config.RootPath);

        // Write to a temp file then move into place so a crash mid-write can't corrupt settings.json.
        var tempPath = _config.SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _config.SettingsPath, overwrite: true);

        Changed?.Invoke(Current);
    }
}
