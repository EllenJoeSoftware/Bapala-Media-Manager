using BapalaServer.Models;

namespace BapalaServer.Repositories;

public interface IMediaRepository
{
    Task<IEnumerable<MediaItem>> GetAllAsync(
        int page, int limit, MediaType? type, string? genre, string? search, bool favoritesOnly);
    Task<int> CountAsync(MediaType? type, string? genre, string? search, bool favoritesOnly);
    Task<MediaItem?> GetByIdAsync(int id);
    Task<MediaItem?> GetByFilePathAsync(string filePath);
    Task<MediaItem> AddAsync(MediaItem item);
    Task<MediaItem> UpdateAsync(MediaItem item);
    Task DeleteAsync(int id);
    Task<WatchHistory?> GetWatchHistoryAsync(int mediaItemId);
    Task UpsertWatchHistoryAsync(int mediaItemId, long progressSeconds);
}
