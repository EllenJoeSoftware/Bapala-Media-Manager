namespace BapalaServer.Models;

/// <summary>
/// Aggregate statistics about the media library, returned by GET /api/media/stats.
/// </summary>
public class LibraryStats
{
    public int TotalItems      { get; set; }
    public int Movies          { get; set; }
    public int Series          { get; set; }
    public int Documentaries   { get; set; }
    public int Education       { get; set; }
    public int MusicVideos     { get; set; }
    public int Favorites       { get; set; }
    public int InProgress      { get; set; }   // started but not finished
    public long TotalDurationSeconds { get; set; }
}
