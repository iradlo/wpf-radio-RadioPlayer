using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RadioPlayer.Models;
using RadioPlayer.Services;

namespace RadioPlayer.ViewModels;

/// <summary>
/// View model for the player bar: current station, transport controls,
/// volume/mute and playback status.
/// </summary>
public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IFavoritesService _favoritesService;
    private readonly ISettingsService _settingsService;
    private readonly IRadioBrowserService _radioBrowserService;
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly SynchronizationContext? _syncContext;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavoriteCommand))]
    private RadioStation? _currentStation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaybackActive))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private PlaybackStatus _status = PlaybackStatus.Stopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _statusMessage;

    /// <summary>Current song title from ICY stream metadata, when available.</summary>
    [ObservableProperty]
    private string? _nowPlaying;

    /// <summary>Volume for the UI slider, 0–100.</summary>
    [ObservableProperty]
    private double _volume;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isCurrentFavorite;

    /// <summary>Creates the view model and restores volume/mute from settings.</summary>
    public PlayerViewModel(
        IAudioPlayerService audioPlayer,
        IFavoritesService favoritesService,
        ISettingsService settingsService,
        IRadioBrowserService radioBrowserService,
        ILogger<PlayerViewModel> logger)
    {
        _audioPlayer = audioPlayer;
        _favoritesService = favoritesService;
        _settingsService = settingsService;
        _radioBrowserService = radioBrowserService;
        _logger = logger;
        _syncContext = SynchronizationContext.Current;

        Volume = Math.Clamp(settingsService.Settings.Volume, 0.0, 1.0) * 100.0;
        IsMuted = settingsService.Settings.IsMuted;
        CurrentStation = settingsService.Settings.LastStation;
        UpdateIsCurrentFavorite();

        _audioPlayer.StatusChanged += OnPlayerStatusChanged;
        _audioPlayer.MetadataChanged += OnPlayerMetadataChanged;
        _favoritesService.FavoritesChanged += (_, _) => RunOnUiThread(UpdateIsCurrentFavorite);
    }

    /// <summary>True while connecting, playing or reconnecting (drives the Stop button).</summary>
    public bool IsPlaybackActive =>
        Status is PlaybackStatus.Connecting or PlaybackStatus.Playing or PlaybackStatus.Reconnecting;

    /// <summary>User-facing status line, e.g. "Playing • 128 kbps • MP3".</summary>
    public string StatusText
    {
        get
        {
            var text = Status switch
            {
                PlaybackStatus.Connecting => "Connecting...",
                PlaybackStatus.Playing => "Playing",
                PlaybackStatus.Reconnecting => StatusMessage ?? "Reconnecting...",
                PlaybackStatus.Error => StatusMessage ?? "Error",
                _ => "Stopped",
            };

            if (Status == PlaybackStatus.Playing && CurrentStation is not null)
            {
                if (CurrentStation.Bitrate > 0)
                {
                    text += $" • {CurrentStation.Bitrate} kbps";
                }

                if (!string.IsNullOrEmpty(CurrentStation.Codec))
                {
                    text += $" • {CurrentStation.Codec}";
                }
            }

            return text;
        }
    }

    /// <summary>Loads favorites so IsFavorite reflects persisted state before the UI becomes interactive.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _favoritesService.GetFavoritesAsync(ct);
        UpdateIsCurrentFavorite();
    }

    /// <summary>Starts playback of the given station and remembers it as the last station.</summary>
    public async Task PlayStationAsync(RadioStation station)
    {
        CurrentStation = station;
        NowPlaying = null;
        UpdateIsCurrentFavorite();

        _settingsService.Settings.LastStation = station;

        RegisterClickInBackground(station.Uuid);
        await _audioPlayer.PlayAsync(station.UrlResolved);
    }

    /// <summary>Plays the current (last selected) station; bound to the Play button.</summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (CurrentStation is not null)
        {
            await PlayStationAsync(CurrentStation);
        }
    }

    private bool CanPlay() => CurrentStation is not null;

    /// <summary>Stops playback and releases stream resources; bound to the Stop button.</summary>
    [RelayCommand(CanExecute = nameof(IsPlaybackActive))]
    private void Stop() => _audioPlayer.Stop();

    /// <summary>Space-bar toggle: stops when active, plays the current station otherwise.</summary>
    [RelayCommand]
    private async Task TogglePlayStopAsync()
    {
        if (IsPlaybackActive)
        {
            _audioPlayer.Stop();
        }
        else if (CurrentStation is not null)
        {
            await PlayStationAsync(CurrentStation);
        }
    }

    /// <summary>Adds or removes the current station from favorites.</summary>
    [RelayCommand(CanExecute = nameof(CanToggleFavorite))]
    private async Task ToggleFavoriteAsync()
    {
        if (CurrentStation is null)
        {
            return;
        }

        if (IsCurrentFavorite)
        {
            await _favoritesService.RemoveAsync(CurrentStation.Uuid);
        }
        else
        {
            await _favoritesService.AddAsync(CurrentStation);
        }
    }

    private bool CanToggleFavorite() => CurrentStation is not null;

    partial void OnVolumeChanged(double value)
    {
        var normalized = Math.Clamp(value, 0.0, 100.0) / 100.0;
        _audioPlayer.Volume = normalized;
        _settingsService.Settings.Volume = normalized;
    }

    partial void OnIsMutedChanged(bool value)
    {
        _audioPlayer.IsMuted = value;
        _settingsService.Settings.IsMuted = value;
    }

    private void OnPlayerStatusChanged(object? sender, PlaybackStatusEventArgs e) =>
        RunOnUiThread(() =>
        {
            StatusMessage = e.Message;
            Status = e.Status;
            if (e.Status is not PlaybackStatus.Playing)
            {
                NowPlaying = null;
            }
        });

    private void OnPlayerMetadataChanged(object? sender, string title) =>
        RunOnUiThread(() => NowPlaying = title);

    private void RegisterClickInBackground(string stationUuid) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await _radioBrowserService.RegisterClickAsync(stationUuid);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Click tracking failed for station {Uuid}", stationUuid);
            }
        });

    private void UpdateIsCurrentFavorite() =>
        IsCurrentFavorite = CurrentStation is not null && _favoritesService.IsFavorite(CurrentStation.Uuid);

    private void RunOnUiThread(Action action)
    {
        if (_syncContext is not null && _syncContext != SynchronizationContext.Current)
        {
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }
}
