using BapalaServer.Models;

namespace BapalaServer.Repositories;

public interface IMediaRepository
{
    Task<IEnumerable<MediaItem>> GetAllAsync(
        int page, int limit, MediaType? type, string? genre, string? search, bool favoritesOnly,
        string sortBy = "dateAdded", bool sortDesc = true);
    Task<int> CountAsync(MediaType? type, string? genre, string? search, bool favoritesOnly);
    Task<MediaItem?> GetByIdAsync(int id);
    Task<MediaItem?> GetByFilePathAsync(string filePath);
    Task<MediaItem> AddAsync(MediaItem item);
    Task<MediaItem> UpdateAsync(MediaItem item);
    Task DeleteAsync(int id);
    Task<WatchHistory?> GetWatchHistoryAsync(int mediaItemId);
    Task UpsertWatchHistoryAsync(int mediaItemId, long progressSeconds);

    /// <summary>
    /// Returns media items the user has started but not finished, newest first.
    /// "In progress" means: progress > 30 s AND progress < 95 % of duration.
    /// </summary>
    Task<IEnumerable<(MediaItem Item, WatchHistory History)>> GetContinueWatchingAsync(int limit = 20);

    /// <summary>
    /// Returns aggregate counts for the library stats endpoint.
    /// </summary>
    Task<LibraryStats> GetLibraryStatsAsync();
}
