namespace RadioPlayer.Services;

/// <summary>Playback state of the audio engine.</summary>
public enum PlaybackStatus
{
    /// <summary>Nothing is playing.</summary>
    Stopped,

    /// <summary>Connecting to the stream or buffering.</summary>
    Connecting,

    /// <summary>Audio is playing.</summary>
    Playing,

    /// <summary>The stream dropped; a reconnect attempt is in progress.</summary>
    Reconnecting,

    /// <summary>Playback failed and was given up.</summary>
    Error,
}

/// <summary>Event data for <see cref="IAudioPlayerService.StatusChanged"/>.</summary>
public sealed class PlaybackStatusEventArgs : EventArgs
{
    /// <summary>The new playback status.</summary>
    public required PlaybackStatus Status { get; init; }

    /// <summary>Optional user-facing message (e.g. error description).</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Audio playback engine for internet radio streams. Implementations must be
/// thread-safe for the public members and must never throw from <see cref="Stop"/>.
/// </summary>
public interface IAudioPlayerService : IDisposable
{
    /// <summary>Raised when playback status changes. May fire on a background thread.</summary>
    event EventHandler<PlaybackStatusEventArgs>? StatusChanged;

    /// <summary>Raised when ICY metadata (current song title) changes. May fire on a background thread.</summary>
    event EventHandler<string>? MetadataChanged;

    /// <summary>Current playback status.</summary>
    PlaybackStatus Status { get; }

    /// <summary>Volume in the 0.0–1.0 range. Applied immediately to an active stream.</summary>
    double Volume { get; set; }

    /// <summary>Mutes/unmutes output without losing the volume setting.</summary>
    bool IsMuted { get; set; }

    /// <summary>
    /// Starts playing the given stream URL. Any current playback is stopped first.
    /// Errors are reported via <see cref="StatusChanged"/>, not thrown.
    /// </summary>
    Task PlayAsync(string streamUrl, CancellationToken ct = default);

    /// <summary>Stops playback and releases the stream connection and buffers.</summary>
    void Stop();
}
