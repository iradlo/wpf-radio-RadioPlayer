using RadioPlayer.Models;

namespace RadioPlayer.Tests.Models;

public sealed class RadioStationTests
{
    [Fact]
    public void PrimaryTag_ReturnsFirstTrimmedTag()
    {
        var station = new RadioStation { Tags = " rock, pop,jazz" };

        Assert.Equal("rock", station.PrimaryTag);
    }

    [Fact]
    public void PrimaryTag_WhenNoTags_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, new RadioStation().PrimaryTag);
    }

    [Theory]
    [InlineData(128, "128 kbps")]
    [InlineData(0, "")]
    public void BitrateDisplay_FormatsKnownBitrateOnly(int bitrate, string expected)
    {
        Assert.Equal(expected, new RadioStation { Bitrate = bitrate }.BitrateDisplay);
    }

    [Fact]
    public void Equals_ComparesByUuid()
    {
        var a = new RadioStation { Uuid = "same", Name = "A" };
        var b = new RadioStation { Uuid = "same", Name = "B" };
        var c = new RadioStation { Uuid = "other", Name = "A" };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
