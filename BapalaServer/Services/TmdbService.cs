using System.Text.Json;
using BapalaServer.Models;

namespace BapalaServer.Services;

public class TmdbService(IConfiguration config, IHttpClientFactory httpFactory) : ITmdbService
{
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const string ImageBase = "https://image.tmdb.org/t/p/w500";

    public async Task<TmdbMetadata?> FetchMetadataAsync(
        string title, int? year, MediaType type, CancellationToken ct = default)
    {
        var apiKey = config["Bapala:TmdbApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var http = httpFactory.CreateClient();
        var endpoint = type == MediaType.Series ? "tv" : "movie";
        var searchUrl = $"{BaseUrl}/search/{endpoint}?api_key={apiKey}&query={Uri.EscapeDataString(title)}";
        if (year.HasValue) searchUrl += $"&year={year}";

        using var searchResp = await http.GetAsync(searchUrl, ct);
        if (!searchResp.IsSuccessStatusCode) return null;

        using var searchDoc = await JsonDocument.ParseAsync(
            await searchResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var results = searchDoc.RootElement.GetProperty("results");
        if (results.GetArrayLength() == 0) return null;

        var top = results[0];
        var tmdbId = top.GetProperty("id").GetInt32();
        var description = top.TryGetProperty("overview", out var ov) ? ov.GetString() : null;
        var rating = top.TryGetProperty("vote_average", out var ra) ? ra.GetDouble() : (double?)null;
        var posterPath = top.TryGetProperty("poster_path", out var pp) && pp.ValueKind != JsonValueKind.Null
            ? ImageBase + pp.GetString() : null;
        var backdropPath = top.TryGetProperty("backdrop_path", out var bp) && bp.ValueKind != JsonValueKind.Null
            ? ImageBase + bp.GetString() : null;

        // Fetch genre names from detail endpoint
        var detailUrl = $"{BaseUrl}/{endpoint}/{tmdbId}?api_key={apiKey}";
        using var detailResp = await http.GetAsync(detailUrl, ct);
        var genres = string.Empty;
        if (detailResp.IsSuccessStatusCode)
        {
            using var detailDoc = await JsonDocument.ParseAsync(
                await detailResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (detailDoc.RootElement.TryGetProperty("genres", out var genreArr))
                genres = string.Join(",", genreArr.EnumerateArray()
                    .Select(g => g.GetProperty("name").GetString()));
        }

        return new TmdbMetadata(tmdbId, description, genres, rating, posterPath, backdropPath);
    }
}
