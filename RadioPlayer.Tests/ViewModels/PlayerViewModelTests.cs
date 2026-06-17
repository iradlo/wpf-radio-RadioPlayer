using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RadioPlayer.Models;
using RadioPlayer.Services;
using RadioPlayer.ViewModels;

namespace RadioPlayer.Tests.ViewModels;

public sealed class PlayerViewModelTests
{
    private readonly Mock<IAudioPlayerService> _audio = new();
    private readonly Mock<IFavoritesService> _favorites = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly Mock<IRadioBrowserService> _radioBrowser = new();
    private readonly AppSettings _appSettings = new();

    private PlayerViewModel CreateViewModel()
    {
        _audio.SetupProperty(a => a.Volume);
        _audio.SetupProperty(a => a.IsMuted);
        _settings.SetupGet(s => s.Settings).Returns(_appSettings);
        return new PlayerViewModel(
            _audio.Object,
            _favorites.Object,
            _settings.Object,
            _radioBrowser.Object,
            NullLogger<PlayerViewModel>.Instance);
    }

    private static RadioStation Station(string uuid = "uuid-1") => new()
    {
        Uuid = uuid,
        Name = "Test FM",
        UrlResolved = "http://example.com/stream.mp3",
        Bitrate = 128,
        Codec = "MP3",
    };

    [Fact]
    public void Constructor_RestoresVolumeAndMuteFromSettings()
    {
        _appSettings.Volume = 0.5;
        _appSettings.IsMuted = true;

        var vm = CreateViewModel();

        Assert.Equal(50.0, vm.Volume);
        Assert.True(vm.IsMuted);
        Assert.Equal(0.5, _audio.Object.Volume);
        Assert.True(_audio.Object.IsMuted);
    }

    [Fact]
    public async Task PlayStationAsync_StartsStreamAndRemembersStation()
    {
        var vm = CreateViewModel();
        var station = Station();

        await vm.PlayStationAsync(station);

        _audio.Verify(a => a.PlayAsync(station.UrlResolved, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Same(station, vm.CurrentStation);
        Assert.Same(station, _appSettings.LastStation);
    }

    [Fact]
    public void VolumeChange_PropagatesToPlayerAndSettings()
    {
        var vm = CreateViewModel();

        vm.Volume = 80;

        Assert.Equal(0.8, _audio.Object.Volume, precision: 5);
        Assert.Equal(0.8, _appSettings.Volume, precision: 5);
    }

    [Fact]
    public void MuteChange_PropagatesToPlayerAndSettings()
    {
        var vm = CreateViewModel();

        vm.IsMuted = true;

        Assert.True(_audio.Object.IsMuted);
        Assert.True(_appSettings.IsMuted);
    }

    [Fact]
    public void StopCommand_StopsPlayer()
    {
        var vm = CreateViewModel();
        _audio.Raise(a => a.StatusChanged += null!, _audio.Object,
            new PlaybackStatusEventArgs { Status = PlaybackStatus.Playing });

        vm.StopCommand.Execute(null);

        _audio.Verify(a => a.Stop(), Times.Once);
    }

    [Fact]
    public void StatusChanged_UpdatesStatusText()
    {
        var vm = CreateViewModel();

        _audio.Raise(a => a.StatusChanged += null!, _audio.Object,
            new PlaybackStatusEventArgs { Status = PlaybackStatus.Connecting });

        Assert.Equal(PlaybackStatus.Connecting, vm.Status);
        Assert.Equal("Connecting...", vm.StatusText);
        Assert.True(vm.IsPlaybackActive);
    }

    [Fact]
    public async Task StatusText_WhilePlaying_IncludesBitrateAndCodec()
    {
        var vm = CreateViewModel();
        await vm.PlayStationAsync(Station());

        _audio.Raise(a => a.StatusChanged += null!, _audio.Object,
            new PlaybackStatusEventArgs { Status = PlaybackStatus.Playing });

        Assert.Equal("Playing • 128 kbps • MP3", vm.StatusText);
    }

    [Fact]
    public void MetadataChanged_SetsNowPlaying_AndStopClearsIt()
    {
        var vm = CreateViewModel();

        _audio.Raise(a => a.MetadataChanged += null!, _audio.Object, "Artist - Song");
        Assert.Equal("Artist - Song", vm.NowPlaying);

        _audio.Raise(a => a.StatusChanged += null!, _audio.Object,
            new PlaybackStatusEventArgs { Status = PlaybackStatus.Stopped });
        Assert.Null(vm.NowPlaying);
    }

    [Fact]
    public async Task ToggleFavorite_AddsWhenNotFavorite()
    {
        var vm = CreateViewModel();
        var station = Station();
        _favorites.Setup(f => f.IsFavorite(station.Uuid)).Returns(false);
        await vm.PlayStationAsync(station);

        await vm.ToggleFavoriteCommand.ExecuteAsync(null);

        _favorites.Verify(f => f.AddAsync(station, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToggleFavorite_RemovesWhenAlreadyFavorite()
    {
        var vm = CreateViewModel();
        var station = Station();
        _favorites.Setup(f => f.IsFavorite(station.Uuid)).Returns(true);
        await vm.PlayStationAsync(station);

        await vm.ToggleFavoriteCommand.ExecuteAsync(null);

        _favorites.Verify(f => f.RemoveAsync(station.Uuid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void PlayCommand_WithoutStation_CannotExecute()
    {
        var vm = CreateViewModel();

        Assert.False(vm.PlayCommand.CanExecute(null));
    }
}
