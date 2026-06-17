using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPlayer.Models;

namespace RadioPlayer.Services;

/// <summary>
/// File-based implementation of <see cref="IFavoritesService"/> storing the full
/// station objects so favorites remain usable offline.
/// </summary>
public sealed class FavoritesService : IFavoritesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly ILogger<FavoritesService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<RadioStation>? _favorites;

    /// <summary>
    /// Creates the service. <paramref name="baseDirectory"/> overrides the storage
    /// folder (used by tests); defaults to %AppData%\RadioPlayer.
    /// </summary>
    public FavoritesService(ILogger<FavoritesService> logger, string? baseDirectory = null)
    {
        _logger = logger;
        _filePath = Path.Combine(baseDirectory ?? AppPaths.AppDataFolder, AppPaths.FavoritesFileName);
    }

    /// <inheritdoc />
    public event EventHandler? FavoritesChanged;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RadioStation>> GetFavoritesAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return (await EnsureLoadedAsync(ct)).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public bool IsFavorite(string stationUuid) =>
        _favorites?.Any(s => s.Uuid == stationUuid) ?? false;

    /// <inheritdoc />
    public async Task AddAsync(RadioStation station, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var favorites = await EnsureLoadedAsync(ct);
            if (favorites.Any(s => s.Uuid == station.Uuid))
            {
                return;
            }

            favorites.Add(station);
            await SaveAsync(ct);
        }
        finally
        {
            _gate.Release();
        }

        FavoritesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string stationUuid, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var favorites = await EnsureLoadedAsync(ct);
            if (favorites.RemoveAll(s => s.Uuid == stationUuid) == 0)
            {
                return;
            }

            await SaveAsync(ct);
        }
        finally
        {
            _gate.Release();
        }

        FavoritesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<List<RadioStation>> EnsureLoadedAsync(CancellationToken ct)
    {
        if (_favorites is not null)
        {
            return _favorites;
        }

        try
        {
            if (File.Exists(_filePath))
            {
                await using var stream = File.OpenRead(_filePath);
                _favorites = await JsonSerializer.DeserializeAsync<List<RadioStation>>(stream, JsonOptions, ct);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to load favorites from {Path}; starting empty", _filePath);
        }

        return _favorites ??= [];
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, _favorites, JsonOptions, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to save favorites to {Path}", _filePath);
        }
    }
}
