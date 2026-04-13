namespace BapalaApp.Models;

public class MediaListResult
{
    public List<MediaItem> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
}

public class UpdateMediaRequest
{
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? Type { get; set; }
    public string? Description { get; set; }
    public string? Genres { get; set; }
    public double? Rating { get; set; }
}

public class WatchProgressResponse
{
    public long ProgressSeconds { get; set; }
}
