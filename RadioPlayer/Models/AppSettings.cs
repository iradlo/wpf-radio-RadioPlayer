namespace RadioPlayer.Models;

/// <summary>
/// User settings and remembered application state, persisted to settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Volume in the 0.0–1.0 range.</summary>
    public double Volume { get; set; } = 0.7;

    /// <summary>Whether audio output is muted.</summary>
    public bool IsMuted { get; set; }

    /// <summary>The station that was playing (or selected) last, for restore on startup.</summary>
    public RadioStation? LastStation { get; set; }

    /// <summary>When true, the last played station starts automatically on launch.</summary>
    public bool AutoPlayLastStation { get; set; }

    /// <summary>Saved window left position; null until the window has been moved/saved once.</summary>
    public double? WindowLeft { get; set; }

    /// <summary>Saved window top position.</summary>
    public double? WindowTop { get; set; }

    /// <summary>Saved window width.</summary>
    public double? WindowWidth { get; set; }

    /// <summary>Saved window height.</summary>
    public double? WindowHeight { get; set; }
}
