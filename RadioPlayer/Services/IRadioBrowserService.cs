using RadioPlayer.Models;

namespace RadioPlayer.Services;

/// <summary>
/// Client for the Radio Browser API (https://api.radio-browser.info).
/// </summary>
public interface IRadioBrowserService
{
    /// <summary>Gets the most popular stations ordered by click count.</summary>
    Task<IReadOnlyList<RadioStation>> GetTopStationsAsync(int limit, CancellationToken ct = default);

    /// <summary>Searches stations by name.</summary>
    Task<IReadOnlyList<RadioStation>> SearchStationsAsync(string name, int limit, CancellationToken ct = default);

    /// <summary>Gets stations for a given country, ordered by popularity.</summary>
    Task<IReadOnlyList<RadioStation>> GetStationsByCountryAsync(string country, int limit, CancellationToken ct = default);

    /// <summary>Gets stations for a given genre tag, ordered by popularity.</summary>
    Task<IReadOnlyList<RadioStation>> GetStationsByTagAsync(string tag, int limit, CancellationToken ct = default);

    /// <summary>Gets country names that have stations, ordered by station count.</summary>
    Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default);

    /// <summary>Gets the most used genre tags, ordered by station count.</summary>
    Task<IReadOnlyList<string>> GetTopTagsAsync(int limit, CancellationToken ct = default);

    /// <summary>Notifies the API that a station was played (click tracking, API etiquette).</summary>
    Task RegisterClickAsync(string stationUuid, CancellationToken ct = default);
}
