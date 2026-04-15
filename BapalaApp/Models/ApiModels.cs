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

/// <summary>
/// A media item returned from GET /api/media/continue-watching,
/// including the saved watch position.
/// </summary>
public class ContinueWatchingItem
{
    public int     Id              { get; set; }
    public string  Title           { get; set; } = string.Empty;
    public int?    Year            { get; set; }
    public string  Type            { get; set; } = "Movie";
    public string? PosterPath      { get; set; }
    public string? BackdropPath    { get; set; }
    public long?   DurationSeconds { get; set; }
    public double? Rating          { get; set; }
    public bool    IsFavorite      { get; set; }
    public long    ProgressSeconds { get; set; }
    public DateTime WatchedAt     { get; set; }

    // ── Computed display helpers ──────────────────────────────────────────────

    /// <summary>Progress as a value between 0.0 and 1.0.</summary>
    public double ProgressFraction =>
        DurationSeconds is > 0
            ? Math.Clamp((double)ProgressSeconds / DurationSeconds.Value, 0, 1)
            : 0;

    /// <summary>Remaining time label, e.g. "45 min left".</summary>
    public string RemainingLabel
    {
        get
        {
            if (DurationSeconds is null or 0) return string.Empty;
            var remaining = DurationSeconds.Value - ProgressSeconds;
            if (remaining <= 0) return "Finished";
            var mins = (int)Math.Ceiling(remaining / 60.0);
            return mins >= 60
                ? $"{mins / 60}h {mins % 60}m left"
                : $"{mins} min left";
        }
    }
}

/// <summary>Library-level aggregate statistics from GET /api/media/stats.</summary>
public class LibraryStats
{
    public int  TotalItems           { get; set; }
    public int  Movies               { get; set; }
    public int  Series               { get; set; }
    public int  Documentaries        { get; set; }
    public int  Education            { get; set; }
    public int  MusicVideos          { get; set; }
    public int  Favorites            { get; set; }
    public int  InProgress           { get; set; }
    public long TotalDurationSeconds { get; set; }

    public string TotalDurationDisplay
    {
        get
        {
            var hours = TotalDurationSeconds / 3600;
            return hours >= 1 ? $"{hours:N0} hrs" : $"{TotalDurationSeconds / 60:N0} min";
        }
    }
}
