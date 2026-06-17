using Microsoft.Extensions.Logging.Abstractions;
using RadioPlayer.Models;
using RadioPlayer.Services;

namespace RadioPlayer.Tests.Services;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "RadioPlayerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private SettingsService CreateService() =>
        new(NullLogger<SettingsService>.Instance, _tempDir);

    [Fact]
    public async Task LoadAsync_WhenNoFileExists_UsesDefaults()
    {
        var service = CreateService();

        await service.LoadAsync();

        Assert.Equal(0.7, service.Settings.Volume);
        Assert.False(service.Settings.IsMuted);
        Assert.Null(service.Settings.LastStation);
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsAllValues()
    {
        var service = CreateService();
        service.Settings.Volume = 0.42;
        service.Settings.IsMuted = true;
        service.Settings.AutoPlayLastStation = true;
        service.Settings.WindowWidth = 1024;
        service.Settings.LastStation = new RadioStation { Uuid = "abc", Name = "Test FM" };

        await service.SaveAsync();
        var reloaded = CreateService();
        await reloaded.LoadAsync();

        Assert.Equal(0.42, reloaded.Settings.Volume);
        Assert.True(reloaded.Settings.IsMuted);
        Assert.True(reloaded.Settings.AutoPlayLastStation);
        Assert.Equal(1024, reloaded.Settings.WindowWidth);
        Assert.Equal("Test FM", reloaded.Settings.LastStation?.Name);
    }

    [Fact]
    public async Task LoadAsync_WithCorruptFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, AppPaths.SettingsFileName), "{ not valid json !!");
        var service = CreateService();

        await service.LoadAsync();

        Assert.Equal(0.7, service.Settings.Volume);
    }
}
