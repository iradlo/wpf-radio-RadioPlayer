using System.Text.Json.Serialization;

namespace RadioPlayer.Models;

/// <summary>
/// A radio station as returned by the Radio Browser API.
/// Property names map to the API's JSON fields.
/// </summary>
public sealed class RadioStation
{
    /// <summary>Unique station identifier assigned by Radio Browser.</summary>
    [JsonPropertyName("stationuuid")]
    public string Uuid { get; set; } = string.Empty;

    /// <summary>Display name of the station.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Stream URL with playlists (M3U/PLS) and redirects already resolved.</summary>
    [JsonPropertyName("url_resolved")]
    public string UrlResolved { get; set; } = string.Empty;

    /// <summary>URL of the station logo, if any.</summary>
    [JsonPropertyName("favicon")]
    public string Favicon { get; set; } = string.Empty;

    /// <summary>Country the station broadcasts from.</summary>
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    [JsonPropertyName("countrycode")]
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Comma-separated genre tags.</summary>
    [JsonPropertyName("tags")]
    public string Tags { get; set; } = string.Empty;

    /// <summary>Audio codec of the stream (MP3, AAC, ...).</summary>
    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;

    /// <summary>Stream bitrate in kbps; 0 when unknown.</summary>
    [JsonPropertyName("bitrate")]
    public int Bitrate { get; set; }

    /// <summary>Station home page, if any.</summary>
    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = string.Empty;

    /// <summary>First genre tag, for compact display in the station list.</summary>
    [JsonIgnore]
    public string PrimaryTag =>
        Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

    /// <summary>Bitrate formatted for display, e.g. "128 kbps"; empty when unknown.</summary>
    [JsonIgnore]
    public string BitrateDisplay => Bitrate > 0 ? $"{Bitrate} kbps" : string.Empty;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is RadioStation other && other.Uuid == Uuid;

    /// <inheritdoc />
    public override int GetHashCode() => Uuid.GetHashCode();
}
