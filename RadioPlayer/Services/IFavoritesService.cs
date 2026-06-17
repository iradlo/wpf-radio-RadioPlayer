using RadioPlayer.Models;

namespace RadioPlayer.Services;

/// <summary>
/// Persists the user's favorite stations to favorites.json in the app data folder.
/// </summary>
public interface IFavoritesService
{
    /// <summary>Raised after the favorites collection changes.</summary>
    event EventHandler? FavoritesChanged;

    /// <summary>Loads favorites from disk. Returns an empty list when none are saved.</summary>
    Task<IReadOnlyList<RadioStation>> GetFavoritesAsync(CancellationToken ct = default);

    /// <summary>Returns true when the station is in favorites.</summary>
    bool IsFavorite(string stationUuid);

    /// <summary>Adds a station to favorites and saves to disk.</summary>
    Task AddAsync(RadioStation station, CancellationToken ct = default);

    /// <summary>Removes a station from favorites and saves to disk.</summary>
    Task RemoveAsync(string stationUuid, CancellationToken ct = default);
}
