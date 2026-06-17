using Microsoft.Extensions.Logging.Abstractions;
using RadioPlayer.Models;
using RadioPlayer.Services;

namespace RadioPlayer.Tests.Services;

public sealed class FileCacheServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "RadioPlayerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetAsync_WhenNothingCached_ReturnsNullNotFresh()
    {
        var cache = new FileCacheService(NullLogger<FileCacheService>.Instance, _tempDir);

        var (value, isFresh) = await cache.GetAsync<List<RadioStation>>("missing");

        Assert.Null(value);
        Assert.False(isFresh);
    }

    [Fact]
    public async Task SetThenGet_ReturnsFreshValue()
    {
        var cache = new FileCacheService(NullLogger<FileCacheService>.Instance, _tempDir);
        var stations = new List<RadioStation> { new() { Uuid = "u1", Name = "Rock Radio" } };

        await cache.SetAsync("stations_topclick", stations);
        var (value, isFresh) = await cache.GetAsync<List<RadioStation>>("stations_topclick");

        Assert.True(isFresh);
        Assert.NotNull(value);
        Assert.Equal("Rock Radio", value[0].Name);
    }

    [Fact]
    public async Task GetAsync_AfterExpiry_ReturnsValueButStale()
    {
        var cache = new FileCacheService(
            NullLogger<FileCacheService>.Instance, _tempDir, expiry: TimeSpan.Zero);
        await cache.SetAsync("key", new List<string> { "a" });

        var (value, isFresh) = await cache.GetAsync<List<string>>("key");

        Assert.NotNull(value);
        Assert.False(isFresh);
    }

    [Fact]
    public async Task GetAsync_KeysWithSpecialCharacters_DoNotCollideWithPaths()
    {
        var cache = new FileCacheService(NullLogger<FileCacheService>.Instance, _tempDir);

        await cache.SetAsync("stations_country_Bosnia and Herzegovina", new List<string> { "x" });
        var (value, isFresh) = await cache.GetAsync<List<string>>("stations_country_Bosnia and Herzegovina");

        Assert.True(isFresh);
        Assert.NotNull(value);
    }
}
