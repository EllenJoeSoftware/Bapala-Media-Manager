using Microsoft.EntityFrameworkCore;
using BapalaServer.Data;
using BapalaServer.Models;
using BapalaServer.Repositories;
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

    [Fact]
    public async Task GetAllAsync_FiltersByType()
    {
        await using var db = CreateInMemoryDb();
        var repo = new SqliteMediaRepository(db);
        db.MediaItems.AddRange(
            new MediaItem { Title = "Movie1", Type = MediaType.Movie, FilePath = "/a.mkv", DateAdded = DateTime.UtcNow },
            new MediaItem { Title = "Show1", Type = MediaType.Series, FilePath = "/b.mkv", DateAdded = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var movies = (await repo.GetAllAsync(1, 10, MediaType.Movie, null, null, false)).ToList();
        Assert.Single(movies);
        Assert.Equal("Movie1", movies[0].Title);
    }

    [Fact]
    public async Task GetAllAsync_SearchesByTitle()
    {
        await using var db = CreateInMemoryDb();
        var repo = new SqliteMediaRepository(db);
        db.MediaItems.AddRange(
            new MediaItem { Title = "Inception", Type = MediaType.Movie, FilePath = "/a.mkv", DateAdded = DateTime.UtcNow },
            new MediaItem { Title = "Interstellar", Type = MediaType.Movie, FilePath = "/b.mkv", DateAdded = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var results = (await repo.GetAllAsync(1, 10, null, null, "incep", false)).ToList();
        Assert.Single(results);
        Assert.Equal("Inception", results[0].Title);
    }

    [Fact]
    public async Task UpsertWatchHistory_CreatesAndUpdates()
    {
        await using var db = CreateInMemoryDb();
        var repo = new SqliteMediaRepository(db);
        var item = new MediaItem { Title = "X", Type = MediaType.Movie, FilePath = "/x.mkv", DateAdded = DateTime.UtcNow };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();

        await repo.UpsertWatchHistoryAsync(item.Id, 300);
        var history = await repo.GetWatchHistoryAsync(item.Id);
        Assert.Equal(300, history!.ProgressSeconds);

        await repo.UpsertWatchHistoryAsync(item.Id, 600);
        history = await repo.GetWatchHistoryAsync(item.Id);
        Assert.Equal(600, history!.ProgressSeconds);
    }
}
