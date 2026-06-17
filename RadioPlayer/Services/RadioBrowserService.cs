using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RadioPlayer.Models;

namespace RadioPlayer.Services;

/// <summary>
/// HTTP implementation of <see cref="IRadioBrowserService"/> with DNS-based server
/// discovery, failover across mirrors and a 24h local cache for list endpoints.
/// </summary>
public sealed class RadioBrowserService : IRadioBrowserService
{
    private const string DiscoveryHost = "all.api.radio-browser.info";
    private const string CacheKeyPrefixStations = "stations";

    private static readonly string[] FallbackServers =
    [
        "de1.api.radio-browser.info",
        "de2.api.radio-browser.info",
        "fi1.api.radio-browser.info",
    ];

    private readonly HttpClient _httpClient;
    private readonly ICacheService _cache;
    private readonly ILogger<RadioBrowserService> _logger;
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private IReadOnlyList<string>? _servers;
    private int _preferredServerIndex;

    /// <summary>Creates the service. The client should have a short request timeout.</summary>
    public RadioBrowserService(HttpClient httpClient, ICacheService cache, ILogger<RadioBrowserService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RadioStation>> GetTopStationsAsync(int limit, CancellationToken ct = default) =>
        GetStationsCachedAsync(
            $"{CacheKeyPrefixStations}_topclick_{limit}",
            $"json/stations/topclick/{limit}?hidebroken=true",
            ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RadioStation>> SearchStationsAsync(string name, int limit, CancellationToken ct = default) =>
        await GetJsonWithFailoverAsync<List<RadioStation>>(
            $"json/stations/search?name={Uri.EscapeDataString(name)}&limit={limit}&hidebroken=true&order=clickcount&reverse=true",
            ct) ?? [];

    /// <inheritdoc />
    public Task<IReadOnlyList<RadioStation>> GetStationsByCountryAsync(string country, int limit, CancellationToken ct = default) =>
        GetStationsCachedAsync(
            $"{CacheKeyPrefixStations}_country_{country}",
            $"json/stations/search?country={Uri.EscapeDataString(country)}&limit={limit}&hidebroken=true&order=clickcount&reverse=true",
            ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<RadioStation>> GetStationsByTagAsync(string tag, int limit, CancellationToken ct = default) =>
        GetStationsCachedAsync(
            $"{CacheKeyPrefixStations}_tag_{tag}",
            $"json/stations/search?tag={Uri.EscapeDataString(tag)}&limit={limit}&hidebroken=true&order=clickcount&reverse=true",
            ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default)
    {
        const string cacheKey = "countries";
        var (cached, isFresh) = await _cache.GetAsync<List<string>>(cacheKey, ct);
        if (cached is not null && isFresh)
        {
            return cached;
        }

        try
        {
            var countries = await GetJsonWithFailoverAsync<List<NamedCount>>(
                "json/countries?order=stationcount&reverse=true&hidebroken=true", ct);
            var names = (countries ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => c.Name)
                .ToList();
            await _cache.SetAsync(cacheKey, names, ct);
            return names;
        }
        catch (Exception ex) when (IsNetworkError(ex, ct))
        {
            _logger.LogWarning(ex, "Failed to fetch countries; using cached data");
            return cached ?? [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetTopTagsAsync(int limit, CancellationToken ct = default)
    {
        var cacheKey = $"tags_{limit}";
        var (cached, isFresh) = await _cache.GetAsync<List<string>>(cacheKey, ct);
        if (cached is not null && isFresh)
        {
            return cached;
        }

        try
        {
            var tags = await GetJsonWithFailoverAsync<List<NamedCount>>(
                $"json/tags?order=stationcount&reverse=true&hidebroken=true&limit={limit}", ct);
            var names = (tags ?? [])
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => t.Name)
                .ToList();
            await _cache.SetAsync(cacheKey, names, ct);
            return names;
        }
        catch (Exception ex) when (IsNetworkError(ex, ct))
        {
            _logger.LogWarning(ex, "Failed to fetch tags; using cached data");
            return cached ?? [];
        }
    }

    /// <inheritdoc />
    public async Task RegisterClickAsync(string stationUuid, CancellationToken ct = default)
    {
        try
        {
            await GetJsonWithFailoverAsync<JsonElement>($"json/url/{stationUuid}", ct);
        }
        catch (Exception ex) when (IsNetworkError(ex, ct))
        {
            // Click tracking is best-effort; never disturb playback because of it.
            _logger.LogDebug(ex, "Click tracking failed for {Uuid}", stationUuid);
        }
    }

    /// <summary>
    /// Serves the station list from a fresh cache entry when possible; otherwise fetches
    /// from the API and caches the result, falling back to stale cache when offline.
    /// </summary>
    private async Task<IReadOnlyList<RadioStation>> GetStationsCachedAsync(
        string cacheKey, string requestPath, CancellationToken ct)
    {
        var (cached, isFresh) = await _cache.GetAsync<List<RadioStation>>(cacheKey, ct);
        if (cached is not null && isFresh)
        {
            return cached;
        }

        try
        {
            var stations = await GetJsonWithFailoverAsync<List<RadioStation>>(requestPath, ct) ?? [];
            await _cache.SetAsync(cacheKey, stations, ct);
            return stations;
        }
        catch (Exception ex) when (IsNetworkError(ex, ct) && cached is not null)
        {
            _logger.LogWarning(ex, "API unavailable; serving stale cache for {Key}", cacheKey);
            return cached;
        }
    }

    /// <summary>Performs a GET trying each known API mirror until one succeeds.</summary>
    private async Task<T?> GetJsonWithFailoverAsync<T>(string requestPath, CancellationToken ct)
    {
        var servers = await GetServersAsync(ct);
        Exception? lastError = null;
        for (var i = 0; i < servers.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var serverIndex = (_preferredServerIndex + i) % servers.Count;
            var url = $"https://{servers[serverIndex]}/{requestPath}";
            try
            {
                var result = await _httpClient.GetFromJsonAsync<T>(url, ct);
                _preferredServerIndex = serverIndex;
                return result;
            }
            catch (Exception ex) when (IsNetworkError(ex, ct))
            {
                _logger.LogWarning(ex, "Radio Browser request failed on {Server}", servers[serverIndex]);
                lastError = ex;
            }
        }

        throw new HttpRequestException("All Radio Browser servers are unreachable.", lastError);
    }

    /// <summary>
    /// Resolves available API mirrors via DNS (all.api.radio-browser.info) with reverse
    /// lookups for hostnames; falls back to a known server list when discovery fails.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetServersAsync(CancellationToken ct)
    {
        if (_servers is not null)
        {
            return _servers;
        }

        await _discoveryGate.WaitAsync(ct);
        try
        {
            if (_servers is not null)
            {
                return _servers;
            }

            var discovered = new List<string>();
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(DiscoveryHost, ct);
                foreach (var address in addresses)
                {
                    try
                    {
                        var entry = await Dns.GetHostEntryAsync(address.ToString(), ct);
                        if (!string.IsNullOrEmpty(entry.HostName) && !discovered.Contains(entry.HostName))
                        {
                            discovered.Add(entry.HostName);
                        }
                    }
                    catch (Exception ex) when (ex is System.Net.Sockets.SocketException)
                    {
                        // Reverse lookup can fail per-address; other mirrors may still resolve.
                    }
                }
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Radio Browser DNS discovery failed; using fallback servers");
            }

            // Randomize to spread load across mirrors (API etiquette).
            _servers = discovered.Count > 0
                ? discovered.OrderBy(_ => Random.Shared.Next()).ToList()
                : FallbackServers;
            return _servers;
        }
        finally
        {
            _discoveryGate.Release();
        }
    }

    private static bool IsNetworkError(Exception ex, CancellationToken userToken) =>
        ex is HttpRequestException or JsonException ||
        (ex is OperationCanceledException && !userToken.IsCancellationRequested);

    /// <summary>Minimal shape of /json/countries and /json/tags entries.</summary>
    private sealed class NamedCount
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stationcount")]
        public int StationCount { get; set; }
    }
}
