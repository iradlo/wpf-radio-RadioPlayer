using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RadioPlayer.Models;
using RadioPlayer.Services;
using RadioPlayer.ViewModels;

namespace RadioPlayer.Tests.ViewModels;

public sealed class StationListViewModelTests
{
    private readonly Mock<IRadioBrowserService> _radioBrowser = new();
    private readonly Mock<IFavoritesService> _favorites = new();

    private StationListViewModel CreateViewModel()
    {
        _radioBrowser
            .Setup(r => r.GetCountriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Serbia", "Germany"]);
        _radioBrowser
            .Setup(r => r.GetTopTagsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["rock", "jazz"]);
        return new StationListViewModel(
            _radioBrowser.Object,
            _favorites.Object,
            NullLogger<StationListViewModel>.Instance);
    }

    private static List<RadioStation> Stations(params string[] names) =>
        names.Select(n => new RadioStation { Uuid = $"uuid-{n}", Name = n }).ToList();

    [Fact]
    public async Task InitializeAsync_LoadsPopularStationsAndFilters()
    {
        _radioBrowser
            .Setup(r => r.GetTopStationsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stations("A", "B"));
        var vm = CreateViewModel();

        await vm.InitializeAsync();

        Assert.Equal(2, vm.Stations.Count);
        Assert.Contains("Serbia", vm.Countries);
        Assert.Contains("rock", vm.Genres);
        Assert.NotNull(vm.SelectedCountry);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task SelectCategory_Favorites_LoadsFromFavoritesService()
    {
        _favorites
            .Setup(f => f.GetFavoritesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stations("Fav"));
        var vm = CreateViewModel();

        await vm.SelectCategoryCommand.ExecuteAsync(StationCategory.Favorites);

        Assert.Single(vm.Stations);
        Assert.Equal("Fav", vm.Stations[0].Station.Name);
        Assert.Equal(StationCategory.Favorites, vm.SelectedCategory);
    }

    [Fact]
    public async Task SelectCategory_ByCountry_QueriesSelectedCountry()
    {
        _radioBrowser
            .Setup(r => r.GetStationsByCountryAsync("Serbia", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stations("Local"));
        var vm = CreateViewModel();
        vm.SelectedCountry = "Serbia";

        await vm.SelectCategoryCommand.ExecuteAsync(StationCategory.ByCountry);

        Assert.Single(vm.Stations);
        Assert.Equal("Local", vm.Stations[0].Station.Name);
    }

    [Fact]
    public async Task SearchText_IsDebounced_AndSearchesByName()
    {
        _radioBrowser
            .Setup(r => r.SearchStationsAsync("jazz", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stations("Jazz FM"));
        var vm = CreateViewModel();

        vm.SearchText = "j";
        vm.SearchText = "ja";
        vm.SearchText = "jazz";
        await Task.Delay(900);

        _radioBrowser.Verify(
            r => r.SearchStationsAsync("jazz", It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _radioBrowser.Verify(
            r => r.SearchStationsAsync("ja", It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Single(vm.Stations);
    }

    [Fact]
    public async Task LoadFailure_SetsNoticeInsteadOfThrowing()
    {
        _radioBrowser
            .Setup(r => r.GetTopStationsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var vm = CreateViewModel();

        await vm.InitializeAsync();

        Assert.NotNull(vm.Notice);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task PlaySelected_RaisesPlayRequestedWithStation()
    {
        _radioBrowser
            .Setup(r => r.GetTopStationsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stations("A"));
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        RadioStation? played = null;
        vm.PlayRequested += (_, station) => played = station;
        vm.SelectedItem = vm.Stations[0];

        vm.PlaySelectedCommand.Execute(null);

        Assert.NotNull(played);
        Assert.Equal("A", played.Name);
    }

    [Fact]
    public async Task ToggleFavorite_OnRow_AddsStation()
    {
        _radioBrowser
            .Setup(r => r.GetTopStationsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stations("A"));
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        var item = vm.Stations[0];
        _favorites.Setup(f => f.IsFavorite(item.Station.Uuid)).Returns(false);

        await vm.ToggleFavoriteCommand.ExecuteAsync(item);

        _favorites.Verify(f => f.AddAsync(item.Station, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyFavorites_ShowsHintNotice()
    {
        _favorites
            .Setup(f => f.GetFavoritesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var vm = CreateViewModel();

        await vm.SelectCategoryCommand.ExecuteAsync(StationCategory.Favorites);

        Assert.NotNull(vm.Notice);
        Assert.Contains("star", vm.Notice, StringComparison.OrdinalIgnoreCase);
    }
}
