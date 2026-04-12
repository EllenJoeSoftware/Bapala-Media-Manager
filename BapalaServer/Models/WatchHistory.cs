namespace BapalaServer.Models;

public class WatchHistory
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public MediaItem MediaItem { get; set; } = null!;
    public long ProgressSeconds { get; set; }
    public DateTime WatchedAt { get; set; }
}
