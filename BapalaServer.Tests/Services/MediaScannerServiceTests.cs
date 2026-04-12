using BapalaServer.Models;
using BapalaServer.Services;
using Xunit;

namespace BapalaServer.Tests.Services;

public class MediaScannerServiceTests
{
    [Theory]
    [InlineData("Inception.2010.1080p.BluRay.mkv", "Inception", 2010, null, null)]
    [InlineData("The.Dark.Knight.(2008).mp4", "The Dark Knight", 2008, null, null)]
    [InlineData("Breaking Bad S01E03.mkv", "Breaking Bad", null, 1, 3)]
    [InlineData("Game.of.Thrones.S05E09.1080p.mkv", "Game of Thrones", null, 5, 9)]
    public void ParseFilename_ExtractsCorrectFields(
        string filename, string expectedTitle, int? expectedYear, int? expectedSeason, int? expectedEpisode)
    {
        var result = MediaScannerService.ParseFilename(filename);
        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedYear, result.Year);
        Assert.Equal(expectedSeason, result.Season);
        Assert.Equal(expectedEpisode, result.Episode);
    }

    [Theory]
    [InlineData("Breaking Bad S01E03.mkv", MediaType.Series)]
    [InlineData("Inception.2010.mkv", MediaType.Movie)]
    public void DetectMediaType_ClassifiesCorrectly(string filename, MediaType expected)
    {
        var type = MediaScannerService.DetectType(filename);
        Assert.Equal(expected, type);
    }

    [Fact]
    public void IsVideoFile_AcceptsCommonFormats()
    {
        Assert.True(MediaScannerService.IsVideoFile("movie.mkv"));
        Assert.True(MediaScannerService.IsVideoFile("movie.mp4"));
        Assert.True(MediaScannerService.IsVideoFile("movie.avi"));
        Assert.False(MediaScannerService.IsVideoFile("photo.jpg"));
        Assert.False(MediaScannerService.IsVideoFile("subtitle.srt"));
    }
}
