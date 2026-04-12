using BapalaServer.Models;

namespace BapalaServer.Services;

public record TmdbMetadata(
    int TmdbId,
    string? Description,
    string? Genres,
    double? Rating,
    string? PosterPath,
    string? BackdropPath);

public interface ITmdbService
{
    Task<TmdbMetadata?> FetchMetadataAsync(
        string title, int? year, MediaType type, CancellationToken ct = default);
}
