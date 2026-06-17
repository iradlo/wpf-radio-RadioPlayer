using RadioPlayer.Models;

namespace RadioPlayer.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to settings.json in the app data folder.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// The current settings. Returns defaults until <see cref="LoadAsync"/> completes
    /// or when no settings file exists.
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>Loads settings from disk; falls back to defaults on missing/corrupt file.</summary>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>Saves the current settings to disk.</summary>
    Task SaveAsync(CancellationToken ct = default);
}
