using Microsoft.EntityFrameworkCore;
using BapalaServer.Data;
using BapalaServer.Models;
using Xunit;

namespace BapalaServer.Tests.Repositories;

public class SqliteMediaRepositoryTests
{
    private static BapalaDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<BapalaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BapalaDbContext(options);
    }

    [Fact]
    public async Task CanAddAndRetrieveMediaItem()
    {
        await using var db = CreateInMemoryDb();
        var item = new MediaItem
        {
            Title = "Inception",
            Year = 2010,
            Type = MediaType.Movie,
            FilePath = "/movies/Inception.mkv",
            DateAdded = DateTime.UtcNow
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();

        var found = await db.MediaItems.FirstAsync(m => m.Title == "Inception");
        Assert.Equal(2010, found.Year);
        Assert.Equal(MediaType.Movie, found.Type);
    }
}
