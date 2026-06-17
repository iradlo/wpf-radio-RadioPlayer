using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using RadioPlayer.Services;

namespace RadioPlayer.ViewModels;

/// <summary>
/// Root view model composing the player and station list view models.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>Creates the root view model and wires station activation to the player.</summary>
    public MainViewModel(
        PlayerViewModel player,
        StationListViewModel stationList,
        ISettingsService settingsService,
        ILogger<MainViewModel> logger)
    {
        Player = player;
        StationList = stationList;
        _settingsService = settingsService;
        _logger = logger;

        StationList.PlayRequested += async (_, station) =>
        {
            try
            {
                await Player.PlayStationAsync(station);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start playback of {Station}", station.Name);
            }
        };
    }

    /// <summary>The player bar view model.</summary>
    public PlayerViewModel Player { get; }

    /// <summary>The station browser view model.</summary>
    public StationListViewModel StationList { get; }

    /// <summary>
    /// Loads initial data (station list, filters) and optionally resumes the last
    /// station. Never throws; errors surface as notices in the UI.
    /// </summary>
    public async Task InitializeAsync()
    {
        await Player.InitializeAsync();
        await StationList.InitializeAsync();

        var settings = _settingsService.Settings;
        if (settings.AutoPlayLastStation && settings.LastStation is not null)
        {
            await Player.PlayStationAsync(settings.LastStation);
        }
    }
}
