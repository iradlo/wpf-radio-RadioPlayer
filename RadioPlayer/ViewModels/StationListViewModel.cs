using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RadioPlayer.Models;
using RadioPlayer.Services;

namespace RadioPlayer.ViewModels;

/// <summary>Sidebar categories of the station browser.</summary>
public enum StationCategory
{
    /// <summary>Locally saved favorite stations.</summary>
    Favorites,

    /// <summary>Most popular stations by click count.</summary>
    Popular,

    /// <summary>Stations filtered by country.</summary>
    ByCountry,

    /// <summary>Stations filtered by genre tag.</summary>
    ByGenre,
}

/// <summary>
/// View model for the station browser: category selection, debounced search
/// and the (virtualized) station list.
/// </summary>
public sealed partial class StationListViewModel : ObservableObject
{
    private const int StationLimit = 50;
    private const int SearchLimit = 100;
    private const int TagLimit = 30;
    private const int MinSearchLength = 2;
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(400);

    private readonly IRadioBrowserService _radioBrowser;
    private readonly IFavoritesService _favoritesService;
    private readonly ILogger<StationListViewModel> _logger;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private StationItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Unobtrusive notification shown above the list (offline mode, errors).</summary>
    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private StationCategory _selectedCategory = StationCategory.Popular;

    [ObservableProperty]
    private string? _selectedCountry;

    [ObservableProperty]
    private string? _selectedGenre;

    /// <summary>Creates the view model. Call <see cref="InitializeAsync"/> to load data.</summary>
    public StationListViewModel(
        IRadioBrowserService radioBrowser,
        IFavoritesService favoritesService,
        ILogger<StationListViewModel> logger)
    {
        _radioBrowser = radioBrowser;
        _favoritesService = favoritesService;
        _logger = logger;
        _favoritesService.FavoritesChanged += OnFavoritesChanged;
    }

    /// <summary>Raised when the user activates a station (double-click, Enter or Play).</summary>
    public event EventHandler<RadioStation>? PlayRequested;

    /// <summary>Stations currently shown in the list.</summary>
    public ObservableCollection<StationItemViewModel> Stations { get; } = [];

    /// <summary>Countries available in the country filter dropdown.</summary>
    public ObservableCollection<string> Countries { get; } = [];

    /// <summary>Genre tags available in the genre filter dropdown.</summary>
    public ObservableCollection<string> Genres { get; } = [];

    /// <summary>
    /// Loads filter dropdowns and the default (popular) station list. Errors are
    /// reported via <see cref="Notice"/>; this method never throws.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _favoritesService.GetFavoritesAsync();
        await LoadCategoryAsync();

        try
        {
            var countries = await _radioBrowser.GetCountriesAsync();
            foreach (var country in countries)
            {
                Countries.Add(country);
            }

            SelectedCountry ??= PickDefaultCountry(countries);

            var genres = await _radioBrowser.GetTopTagsAsync(TagLimit);
            foreach (var genre in genres)
            {
                Genres.Add(genre);
            }

            SelectedGenre ??= genres.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load filter lists");
        }
    }

    /// <summary>Switches the sidebar category and reloads the list.</summary>
    [RelayCommand]
    private async Task SelectCategoryAsync(StationCategory category)
    {
        SelectedCategory = category;
        SearchText = string.Empty;
        await LoadCategoryAsync();
    }

    /// <summary>Requests playback of the selected station.</summary>
    [RelayCommand]
    private void PlaySelected()
    {
        if (SelectedItem is not null)
        {
            PlayRequested?.Invoke(this, SelectedItem.Station);
        }
    }

    /// <summary>Adds/removes a station from favorites (star button on a list row).</summary>
    [RelayCommand]
    private async Task ToggleFavoriteAsync(StationItemViewModel item)
    {
        if (item.IsFavorite)
        {
            await _favoritesService.RemoveAsync(item.Station.Uuid);
        }
        else
        {
            await _favoritesService.AddAsync(item.Station);
        }
    }

    /// <summary>Reloads the current category (used as refresh after errors).</summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadCategoryAsync();

    partial void OnSearchTextChanged(string value) => _ = DebounceSearchAsync(value);

    partial void OnSelectedCountryChanged(string? value)
    {
        if (value is not null && SelectedCategory == StationCategory.ByCountry)
        {
            _ = LoadCategoryAsync();
        }
    }

    partial void OnSelectedGenreChanged(string? value)
    {
        if (value is not null && SelectedCategory == StationCategory.ByGenre)
        {
            _ = LoadCategoryAsync();
        }
    }

    private async Task DebounceSearchAsync(string query)
    {
        var cts = ReplaceLoadCts();
        try
        {
            await Task.Delay(SearchDebounceDelay, cts.Token);

            query = query.Trim();
            if (query.Length < MinSearchLength)
            {
                await LoadIntoListAsync(
                    ct => FetchCategoryAsync(SelectedCategory, ct), cts);
            }
            else
            {
                await LoadIntoListAsync(
                    ct => _radioBrowser.SearchStationsAsync(query, SearchLimit, ct), cts);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer input.
        }
    }

    private Task LoadCategoryAsync() =>
        LoadIntoListAsync(ct => FetchCategoryAsync(SelectedCategory, ct), ReplaceLoadCts());

    private async Task<IReadOnlyList<RadioStation>> FetchCategoryAsync(StationCategory category, CancellationToken ct) =>
        category switch
        {
            StationCategory.Favorites => await _favoritesService.GetFavoritesAsync(ct),
            StationCategory.ByCountry when SelectedCountry is not null =>
                await _radioBrowser.GetStationsByCountryAsync(SelectedCountry, StationLimit, ct),
            StationCategory.ByGenre when SelectedGenre is not null =>
                await _radioBrowser.GetStationsByTagAsync(SelectedGenre, StationLimit, ct),
            StationCategory.ByCountry or StationCategory.ByGenre => [],
            _ => await _radioBrowser.GetTopStationsAsync(StationLimit, ct),
        };

    private async Task LoadIntoListAsync(
        Func<CancellationToken, Task<IReadOnlyList<RadioStation>>> fetch,
        CancellationTokenSource cts)
    {
        IsLoading = true;
        Notice = null;
        try
        {
            var stations = await fetch(cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            Stations.Clear();
            foreach (var station in stations)
            {
                Stations.Add(new StationItemViewModel(station, _favoritesService.IsFavorite(station.Uuid)));
            }

            if (Stations.Count == 0)
            {
                Notice = SelectedCategory == StationCategory.Favorites && string.IsNullOrEmpty(SearchText)
                    ? "No favorites yet — click the star next to a station to add one."
                    : "No stations found.";
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load stations");
            Notice = "Could not reach the station directory. Check your connection and try again.";
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    private CancellationTokenSource ReplaceLoadCts()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        return cts;
    }

    private void OnFavoritesChanged(object? sender, EventArgs e)
    {
        foreach (var item in Stations)
        {
            item.IsFavorite = _favoritesService.IsFavorite(item.Station.Uuid);
        }

        // Keep the favorites view in sync when a station is removed from it.
        if (SelectedCategory == StationCategory.Favorites && string.IsNullOrEmpty(SearchText))
        {
            _ = LoadCategoryAsync();
        }
    }

    private static string? PickDefaultCountry(IReadOnlyList<string> countries)
    {
        var local = RegionInfo.CurrentRegion.EnglishName;
        return countries.FirstOrDefault(c => string.Equals(c, local, StringComparison.OrdinalIgnoreCase))
               ?? countries.FirstOrDefault();
    }
}
