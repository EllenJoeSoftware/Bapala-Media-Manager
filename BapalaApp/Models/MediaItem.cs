namespace BapalaApp.Models;

/// <summary>
/// Mirror of the server's MediaItem entity, kept as plain properties for JSON deserialization.
/// Type is a string (not an enum) so the app tolerates new server types without a rebuild.
/// </summary>
public class MediaItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string Type { get; set; } = "Movie";   // "Movie" | "Series" | "Documentary" | "Education" | "MusicVideo"
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? Description { get; set; }
    public string? Genres { get; set; }            // comma-separated: "Action,Thriller"
    public double? Rating { get; set; }
    public long? DurationSeconds { get; set; }
    public bool IsFavorite { get; set; }
    public int? TmdbId { get; set; }
    public DateTime DateAdded { get; set; }

    // ── Computed display helpers used in XAML bindings ────────────────────────

    /// <summary>Year · Type — shown below poster.</summary>
    public string DisplayMeta =>
        string.Join(" · ", new[] { Year?.ToString(), Type }.Where(x => !string.IsNullOrEmpty(x)));

    /// <summary>Rating formatted to one decimal, or em-dash if not set.</summary>
    public string RatingDisplay => Rating.HasValue ? $"★ {Rating.Value:F1}" : "—";

    /// <summary>Genres as a display string (commas become ", ").</summary>
    public string GenresDisplay =>
        string.IsNullOrWhiteSpace(Genres)
            ? string.Empty
            : string.Join(", ", Genres.Split(',', StringSplitOptions.TrimEntries));

    /// <summary>Favourite icon character.</summary>
    public string FavIcon => IsFavorite ? "♥" : "♡";
}
