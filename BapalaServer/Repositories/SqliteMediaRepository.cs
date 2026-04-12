using Microsoft.EntityFrameworkCore;
using BapalaServer.Data;
using BapalaServer.Models;

namespace BapalaServer.Repositories;

public class SqliteMediaRepository(BapalaDbContext db) : IMediaRepository
{
    public async Task<IEnumerable<MediaItem>> GetAllAsync(
        int page, int limit, MediaType? type, string? genre, string? search, bool favoritesOnly)
    {
        var q = db.MediaItems.AsQueryable();
        if (type.HasValue) q = q.Where(m => m.Type == type.Value);
        if (!string.IsNullOrWhiteSpace(genre))
            q = q.Where(m => m.Genres != null && (
                m.Genres == genre ||
                EF.Functions.Like(m.Genres, genre + ",%") ||
                EF.Functions.Like(m.Genres, "%," + genre) ||
                EF.Functions.Like(m.Genres, "%," + genre + ",%")));
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(m => EF.Functions.Like(m.Title, $"%{search}%"));
        if (favoritesOnly) q = q.Where(m => m.IsFavorite);
        return await q.OrderByDescending(m => m.DateAdded)
                      .Skip((page - 1) * limit).Take(limit)
                      .ToListAsync();
    }

    public async Task<int> CountAsync(MediaType? type, string? genre, string? search, bool favoritesOnly)
    {
        var q = db.MediaItems.AsQueryable();
        if (type.HasValue) q = q.Where(m => m.Type == type.Value);
        if (!string.IsNullOrWhiteSpace(genre))
            q = q.Where(m => m.Genres != null && (
                m.Genres == genre ||
                EF.Functions.Like(m.Genres, genre + ",%") ||
                EF.Functions.Like(m.Genres, "%," + genre) ||
                EF.Functions.Like(m.Genres, "%," + genre + ",%")));
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(m => EF.Functions.Like(m.Title, $"%{search}%"));
        if (favoritesOnly) q = q.Where(m => m.IsFavorite);
        return await q.CountAsync();
    }

    public Task<MediaItem?> GetByIdAsync(int id) =>
        db.MediaItems.FirstOrDefaultAsync(m => m.Id == id);

    public Task<MediaItem?> GetByFilePathAsync(string filePath) =>
        db.MediaItems.FirstOrDefaultAsync(m => m.FilePath == filePath);

    public async Task<MediaItem> AddAsync(MediaItem item)
    {
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    public async Task<MediaItem> UpdateAsync(MediaItem item)
    {
        db.MediaItems.Update(item);
        await db.SaveChangesAsync();
        return item;
    }

    public async Task DeleteAsync(int id)
    {
        var item = await db.MediaItems.FindAsync(id);
        if (item != null)
        {
            db.MediaItems.Remove(item);
            await db.SaveChangesAsync();
        }
    }

    public Task<WatchHistory?> GetWatchHistoryAsync(int mediaItemId) =>
        db.WatchHistory.FirstOrDefaultAsync(w => w.MediaItemId == mediaItemId);

    public async Task UpsertWatchHistoryAsync(int mediaItemId, long progressSeconds)
    {
        var existing = await db.WatchHistory.FirstOrDefaultAsync(w => w.MediaItemId == mediaItemId);
        if (existing == null)
        {
            db.WatchHistory.Add(new WatchHistory
            {
                MediaItemId = mediaItemId,
                ProgressSeconds = progressSeconds,
                WatchedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.ProgressSeconds = progressSeconds;
            existing.WatchedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }
}
