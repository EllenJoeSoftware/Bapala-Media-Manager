namespace BapalaServer.Models;

public class MediaItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public MediaType Type { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? Description { get; set; }
    public string? Genres { get; set; }          // Comma-separated: "Action,Thriller"
    public double? Rating { get; set; }
    public long? DurationSeconds { get; set; }
    public bool IsFavorite { get; set; }
    public int? TmdbId { get; set; }
    public DateTime DateAdded { get; set; }
    public ICollection<WatchHistory> WatchHistory { get; set; } = new List<WatchHistory>();
}
