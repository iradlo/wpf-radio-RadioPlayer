using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RadioPlayer.Services;

/// <summary>
/// File-based implementation of <see cref="ICacheService"/>. Each key is one JSON
/// file in %AppData%\RadioPlayer\cache containing a timestamped envelope.
/// </summary>
public sealed class FileCacheService : ICacheService
{
    private sealed class Envelope<T>
    {
        public DateTimeOffset CreatedAt { get; set; }
        public T? Value { get; set; }
    }

    /// <summary>Default cache freshness window.</summary>
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);

    private readonly string _cacheDirectory;
    private readonly TimeSpan _expiry;
    private readonly ILogger<FileCacheService> _logger;

    /// <summary>
    /// Creates the service. <paramref name="baseDirectory"/> overrides the storage folder
    /// and <paramref name="expiry"/> the freshness window (used by tests).
    /// </summary>
    public FileCacheService(ILogger<FileCacheService> logger, string? baseDirectory = null, TimeSpan? expiry = null)
    {
        _logger = logger;
        _cacheDirectory = Path.Combine(baseDirectory ?? AppPaths.AppDataFolder, AppPaths.CacheFolderName);
        _expiry = expiry ?? DefaultExpiry;
    }

    /// <inheritdoc />
    public async Task<(T? Value, bool IsFresh)> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        var path = GetFilePath(key);
        try
        {
            if (!File.Exists(path))
            {
                return (null, false);
            }

            await using var stream = File.OpenRead(path);
            var envelope = await JsonSerializer.DeserializeAsync<Envelope<T>>(stream, cancellationToken: ct);
            if (envelope?.Value is null)
            {
                return (null, false);
            }

            var isFresh = DateTimeOffset.UtcNow - envelope.CreatedAt < _expiry;
            return (envelope.Value, isFresh);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to read cache entry {Key}", key);
            return (null, false);
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, CancellationToken ct = default) where T : class
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var envelope = new Envelope<T> { CreatedAt = DateTimeOffset.UtcNow, Value = value };
            await using var stream = File.Create(GetFilePath(key));
            await JsonSerializer.SerializeAsync(stream, envelope, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to write cache entry {Key}", key);
        }
    }

    private string GetFilePath(string key)
    {
        var safeKey = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_cacheDirectory, $"{safeKey}.json");
    }
}
