using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPlayer.Models;

namespace RadioPlayer.Services;

/// <summary>
/// File-based implementation of <see cref="ISettingsService"/> storing JSON
/// in the app data folder.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly ILogger<SettingsService> _logger;

    /// <summary>
    /// Creates the service. <paramref name="baseDirectory"/> overrides the storage
    /// folder (used by tests); defaults to %AppData%\RadioPlayer.
    /// </summary>
    public SettingsService(ILogger<SettingsService> logger, string? baseDirectory = null)
    {
        _logger = logger;
        _filePath = Path.Combine(baseDirectory ?? AppPaths.AppDataFolder, AppPaths.SettingsFileName);
    }

    /// <inheritdoc />
    public AppSettings Settings { get; private set; } = new();

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, ct);
            if (loaded is not null)
            {
                Settings = loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to load settings from {Path}; using defaults", _filePath);
            Settings = new AppSettings();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, Settings, JsonOptions, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _filePath);
        }
    }
}
