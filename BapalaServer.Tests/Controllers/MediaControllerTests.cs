using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using BapalaServer.Controllers;
using BapalaServer.Hubs;
using BapalaServer.Models;
using BapalaServer.Repositories;
using BapalaServer.Services;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace BapalaServer.Tests.Controllers;

public class MediaControllerTests
{
    private static MediaController CreateController(IMediaRepository? repo = null)
    {
        repo ??= new Mock<IMediaRepository>().Object;
        var scanner = new Mock<IMediaScannerService>().Object;
        var hubContext = new Mock<IHubContext<ScanProgressHub>>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bapala:MediaFolders"] = "" })
            .Build();
        return new MediaController(repo, scanner, hubContext.Object, config);
    }

    [Fact]
    public async Task GetAll_Returns200WithList()
    {
        var repoMock = new Mock<IMediaRepository>();
        repoMock.Setup(r => r.GetAllAsync(1, 20, null, null, null, false))
                .ReturnsAsync([new MediaItem { Id = 1, Title = "Inception", Type = MediaType.Movie,
                    FilePath = "/a.mkv", DateAdded = DateTime.UtcNow }]);
        repoMock.Setup(r => r.CountAsync(null, null, null, false)).ReturnsAsync(1);

        var result = await CreateController(repoMock.Object).GetAll(1, 20, null, null, null, false) as OkObjectResult;
        Assert.NotNull(result);
        Assert.Equal(200, result!.StatusCode);
    }

    [Fact]
    public async Task GetById_Returns404WhenMissing()
    {
        var repoMock = new Mock<IMediaRepository>();
        repoMock.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((MediaItem?)null);

        var result = await CreateController(repoMock.Object).GetById(99);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ToggleFavorite_FlipsFlag()
    {
        var item = new MediaItem { Id = 1, Title = "X", Type = MediaType.Movie,
            FilePath = "/x.mkv", DateAdded = DateTime.UtcNow, IsFavorite = false };
        var repoMock = new Mock<IMediaRepository>();
        repoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(item);
        repoMock.Setup(r => r.UpdateAsync(It.IsAny<MediaItem>())).ReturnsAsync(item);

        var result = await CreateController(repoMock.Object).ToggleFavorite(1) as OkObjectResult;
        Assert.NotNull(result);
        repoMock.Verify(r => r.UpdateAsync(It.Is<MediaItem>(m => m.IsFavorite)), Times.Once);
    }
}

public class StreamControllerTests
{
    [Fact]
    public void GetMimeType_ReturnsCorrectType()
    {
        Assert.Equal("video/mp4", StreamController.GetMimeType(".mp4"));
        Assert.Equal("video/x-matroska", StreamController.GetMimeType(".mkv"));
        Assert.Equal("video/x-msvideo", StreamController.GetMimeType(".avi"));
        Assert.Equal("application/octet-stream", StreamController.GetMimeType(".xyz"));
    }
}
