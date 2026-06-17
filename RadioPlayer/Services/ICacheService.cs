namespace RadioPlayer.Services;

/// <summary>
/// Local JSON file cache with expiry, stored in the app data folder.
/// Used to serve station lists quickly and to keep the app usable offline.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Reads a cached value. Returns the value even when expired (so the app can work
    /// offline) together with a flag telling whether it is still fresh.
    /// Returns (null, false) when nothing is cached or the file is corrupt.
    /// </summary>
    Task<(T? Value, bool IsFresh)> GetAsync<T>(string key, CancellationToken ct = default) where T : class;

    /// <summary>Writes a value to the cache with the configured expiry window.</summary>
    Task SetAsync<T>(string key, T value, CancellationToken ct = default) where T : class;
}
