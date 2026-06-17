using Microsoft.Extensions.Logging.Abstractions;
using RadioPlayer.Models;
using RadioPlayer.Services;

namespace RadioPlayer.Tests.Services;

public sealed class FavoritesServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "RadioPlayerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private FavoritesService CreateService() =>
        new(NullLogger<FavoritesService>.Instance, _tempDir);

    private static RadioStation Station(string uuid, string name = "Station") =>
        new() { Uuid = uuid, Name = name, UrlResolved = "http://example.com/stream" };

    [Fact]
    public async Task GetFavoritesAsync_WhenEmpty_ReturnsEmptyList()
    {
        var service = CreateService();

        var favorites = await service.GetFavoritesAsync();

        Assert.Empty(favorites);
    }

    [Fact]
    public async Task AddAsync_PersistsAcrossInstances()
    {
        var service = CreateService();
        await service.AddAsync(Station("uuid-1", "Jazz FM"));

        var reloaded = CreateService();
        var favorites = await reloaded.GetFavoritesAsync();

        Assert.Single(favorites);
        Assert.Equal("Jazz FM", favorites[0].Name);
    }

    [Fact]
    public async Task AddAsync_SameStationTwice_AddsOnlyOnce()
    {
        var service = CreateService();

        await service.AddAsync(Station("uuid-1"));
        await service.AddAsync(Station("uuid-1"));

        Assert.Single(await service.GetFavoritesAsync());
    }

    [Fact]
    public async Task RemoveAsync_RemovesStationAndRaisesEvent()
    {
        var service = CreateService();
        await service.AddAsync(Station("uuid-1"));
        var eventCount = 0;
        service.FavoritesChanged += (_, _) => eventCount++;

        await service.RemoveAsync("uuid-1");

        Assert.Empty(await service.GetFavoritesAsync());
        Assert.False(service.IsFavorite("uuid-1"));
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task IsFavorite_AfterAdd_ReturnsTrue()
    {
        var service = CreateService();

        await service.AddAsync(Station("uuid-1"));

        Assert.True(service.IsFavorite("uuid-1"));
        Assert.False(service.IsFavorite("other"));
    }
}
