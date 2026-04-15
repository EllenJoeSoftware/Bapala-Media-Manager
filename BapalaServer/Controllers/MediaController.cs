using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using BapalaServer.Hubs;
using BapalaServer.Models;
using BapalaServer.Repositories;
using BapalaServer.Services;

namespace BapalaServer.Controllers;

[ApiController]
[Route("api/media")]
[Authorize]
public class MediaController(
    IMediaRepository repo,
    IHubContext<ScanProgressHub> hub,
    IConfiguration config,
    IServiceScopeFactory scopeFactory) : ControllerBase
{
    public record MediaListResponse(IEnumerable<MediaItem> Items, int Total, int Page, int Limit);
    public record CreateMediaRequest(string FilePath, string? Title, MediaType Type, int? Year);
    public record UpdateMediaRequest(string? Title, int? Year, string? Description, string? Genres, double? Rating, MediaType? Type);
    public record WatchProgressRequest(long ProgressSeconds);
    public record BulkUpdateTypeRequest(int[] Ids, MediaType Type);

    /// <summary>Add a new media item to the library.</summary>
    /// <response code="201">Item created</response>
    /// <response code="400">FilePath is missing</response>
    /// <response code="409">File path already exists in the library</response>
    [HttpPost]
    [ProducesResponseType<MediaItem>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateMediaRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FilePath))
            return BadRequest(new { error = "FilePath is required." });

        var existing = await repo.GetByFilePathAsync(req.FilePath);
        if (existing != null)
            return Conflict(new { error = "An entry with this file path already exists." });

        var item = new MediaItem
        {
            FilePath = req.FilePath,
            Title = string.IsNullOrWhiteSpace(req.Title)
                ? Path.GetFileNameWithoutExtension(req.FilePath)
                : req.Title,
            Type = req.Type,
            Year = req.Year,
            DateAdded = DateTime.UtcNow
        };
        await repo.AddAsync(item);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    /// <summary>List media items with filtering, search, sorting and pagination.</summary>
    /// <response code="200">Paged list of media items</response>
    [HttpGet]
    [ProducesResponseType<MediaListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] MediaType? type = null,
        [FromQuery] string? genre = null,
        [FromQuery] string? search = null,
        [FromQuery] bool favorites = false,
        [FromQuery] string sortBy = "dateAdded",
        [FromQuery] bool sortDesc = true)
    {
        var items = await repo.GetAllAsync(page, limit, type, genre, search, favorites, sortBy, sortDesc);
        var total = await repo.CountAsync(type, genre, search, favorites);
        return Ok(new MediaListResponse(items, total, page, limit));
    }

    /// <summary>Get a single media item by ID.</summary>
    /// <response code="200">Media item</response>
    /// <response code="404">Not found</response>
    [HttpGet("{id}")]
    [ProducesResponseType<MediaItem>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await repo.GetByIdAsync(id);
        return item == null ? NotFound() : Ok(item);
    }

    /// <summary>Update metadata for a media item.</summary>
    /// <response code="200">Updated item</response>
    /// <response code="404">Not found</response>
    [HttpPut("{id}")]
    [ProducesResponseType<MediaItem>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateMediaRequest req)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        if (req.Title != null) item.Title = req.Title;
        if (req.Year.HasValue) item.Year = req.Year;
        if (req.Description != null) item.Description = req.Description;
        if (req.Genres != null) item.Genres = req.Genres;
        if (req.Rating.HasValue) item.Rating = req.Rating;
        if (req.Type.HasValue) item.Type = req.Type.Value;
        await repo.UpdateAsync(item);
        return Ok(item);
    }

    /// <summary>Remove a media item from the library.</summary>
    /// <response code="204">Deleted successfully</response>
    /// <response code="404">Not found</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        await repo.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>Toggle the favourite flag on a media item.</summary>
    /// <response code="200">Updated item</response>
    [HttpPost("{id}/favorite")]
    [ProducesResponseType<MediaItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ToggleFavorite(int id)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        item.IsFavorite = !item.IsFavorite;
        await repo.UpdateAsync(item);
        return Ok(new { item.IsFavorite });
    }

    /// <summary>Save the current watch position (in seconds).</summary>
    /// <response code="204">Saved</response>
    [HttpPost("{id}/progress")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SaveProgress(int id, [FromBody] WatchProgressRequest req)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        await repo.UpsertWatchHistoryAsync(id, req.ProgressSeconds);
        return NoContent();
    }

    /// <summary>Get the saved watch position for a media item.</summary>
    /// <response code="200">Progress in seconds</response>
    [HttpGet("{id}/progress")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProgress(int id)
    {
        var history = await repo.GetWatchHistoryAsync(id);
        return Ok(new { ProgressSeconds = history?.ProgressSeconds ?? 0 });
    }

    // ── Continue watching ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns media items the authenticated user has started but not finished,
    /// ordered most-recently-watched first.
    /// </summary>
    /// <param name="limit">Maximum number of items to return (default 20, max 50).</param>
    /// <response code="200">List of in-progress items with their saved position.</response>
    [HttpGet("continue-watching")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContinueWatching([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 50);
        var rows = await repo.GetContinueWatchingAsync(limit);

        var result = rows.Select(r => new
        {
            r.Item.Id,
            r.Item.Title,
            r.Item.Year,
            Type          = r.Item.Type.ToString(),
            r.Item.PosterPath,
            r.Item.BackdropPath,
            r.Item.DurationSeconds,
            r.Item.Rating,
            r.Item.IsFavorite,
            r.Item.DateAdded,
            ProgressSeconds = r.History.ProgressSeconds,
            WatchedAt       = r.History.WatchedAt,
        });

        return Ok(result);
    }

    // ── Library statistics ────────────────────────────────────────────────────

    /// <summary>
    /// Returns aggregate counts for the entire media library.
    /// Useful for dashboard / home-screen widgets.
    /// </summary>
    /// <response code="200">Library statistics object.</response>
    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats()
    {
        var stats = await repo.GetLibraryStatsAsync();
        return Ok(stats);
    }

    // ── Bulk section/type change ──────────────────────────────────────────────
    /// <summary>Change the media type for multiple items at once.</summary>
    /// <response code="204">Updated successfully</response>
    [HttpPost("bulk-type")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> BulkUpdateType([FromBody] BulkUpdateTypeRequest req)
    {
        if (req.Ids == null || req.Ids.Length == 0)
            return BadRequest(new { error = "No IDs provided." });

        int updated = 0;
        foreach (var id in req.Ids)
        {
            var item = await repo.GetByIdAsync(id);
            if (item == null) continue;
            item.Type = req.Type;
            await repo.UpdateAsync(item);
            updated++;
        }
        return Ok(new { updated });
    }

    // ── TMDB refresh for a single item ────────────────────────────────────────
    [HttpPost("{id}/refresh-tmdb")]
    public async Task<IActionResult> RefreshTmdb(int id)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();

        await using var scope = scopeFactory.CreateAsyncScope();
        var tmdb = scope.ServiceProvider.GetRequiredService<ITmdbService>();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var meta = await tmdb.FetchMetadataAsync(item.Title, item.Year, item.Type, cts.Token);
            if (meta == null)
                return Ok(new { success = false, message = "No results found on TMDB for this title." });

            item.Description  = meta.Description  ?? item.Description;
            item.Genres       = meta.Genres        ?? item.Genres;
            item.Rating       = meta.Rating        ?? item.Rating;
            item.TmdbId       = meta.TmdbId;
            item.PosterPath   = meta.PosterPath    ?? item.PosterPath;
            item.BackdropPath = meta.BackdropPath  ?? item.BackdropPath;
            await repo.UpdateAsync(item);

            return Ok(new { success = true, message = "Metadata refreshed from TMDB.", item });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, new { success = false, message = "TMDB request timed out." });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { success = false, message = $"TMDB error: {ex.Message}" });
        }
    }

    // ── Bulk TMDB refresh ────────────────────────────────────────────────────
    /// <summary>
    /// Re-fetches TMDB metadata for every item in the library that is missing
    /// a poster, description, or rating.  Runs in the background; progress is
    /// broadcast over the SignalR hub at /hubs/scan using the events:
    ///   TmdbRefreshStarted   { total }
    ///   TmdbRefreshProgress  { processed, total, title, success }
    ///   TmdbRefreshCompleted { updated, skipped, failed, total }
    ///   TmdbRefreshError     { error }
    /// </summary>
    /// <param name="force">
    ///   When true, refreshes all items including those that already have full metadata.
    ///   When false (default), only items missing poster/description/rating are refreshed.
    /// </param>
    [HttpPost("refresh-tmdb-all")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult RefreshTmdbAll([FromQuery] bool force = false)
    {
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var tmdb  = scope.ServiceProvider.GetRequiredService<ITmdbService>();
            var repo2 = scope.ServiceProvider.GetRequiredService<IMediaRepository>();

            try
            {
                // Fetch ALL items (no pagination) — use a large limit
                var all = await repo2.GetAllAsync(
                    page: 1, limit: int.MaxValue,
                    type: null, genre: null, search: null,
                    favoritesOnly: false,
                    sortBy: "dateAdded", sortDesc: false);

                var items = force
                    ? all.ToList()
                    : all.Where(i =>
                          string.IsNullOrWhiteSpace(i.PosterPath)   ||
                          string.IsNullOrWhiteSpace(i.Description)  ||
                          !i.Rating.HasValue).ToList();

                await hub.Clients.All.SendAsync("TmdbRefreshStarted", new { total = items.Count });

                int updated = 0, skipped = 0, failed = 0;

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    bool success = false;

                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        var meta = await tmdb.FetchMetadataAsync(item.Title, item.Year, item.Type, cts.Token);

                        if (meta != null)
                        {
                            item.Description  = meta.Description  ?? item.Description;
                            item.Genres       = meta.Genres        ?? item.Genres;
                            item.Rating       = meta.Rating        ?? item.Rating;
                            item.TmdbId       = meta.TmdbId;
                            item.PosterPath   = meta.PosterPath    ?? item.PosterPath;
                            item.BackdropPath = meta.BackdropPath  ?? item.BackdropPath;
                            await repo2.UpdateAsync(item);
                            updated++;
                            success = true;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch
                    {
                        failed++;
                    }

                    await hub.Clients.All.SendAsync("TmdbRefreshProgress", new
                    {
                        processed = i + 1,
                        total     = items.Count,
                        title     = item.Title,
                        success,
                    });

                    // Respect TMDB rate limit: ~40 requests/10 s → ~250 ms between calls
                    await Task.Delay(260);
                }

                await hub.Clients.All.SendAsync("TmdbRefreshCompleted", new
                {
                    updated,
                    skipped,
                    failed,
                    total = items.Count,
                });
            }
            catch (Exception ex)
            {
                await hub.Clients.All.SendAsync("TmdbRefreshError", new { error = ex.Message });
            }
        });

        return Accepted(new { message = "TMDB refresh started. Connect to /hubs/scan for progress." });
    }

    // ── Library scan ─────────────────────────────────────────────────────────
    /// <summary>Trigger a library scan of the configured media folders.</summary>
    /// <response code="200">Scan started</response>
    [HttpPost("scan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult TriggerScan()
    {
        var folders = config.GetSection("Bapala:MediaFolders").Get<string[]>() ?? [];
        if (folders.Length == 0)
            return BadRequest(new { error = "No media folders configured. Add them in Settings." });

        _ = Task.Run(async () =>
        {
            await hub.Clients.All.SendAsync("ScanStarted", new { folders });
            var progress = new Progress<ScanProgress>(p =>
                hub.Clients.All.SendAsync("ScanProgress", p));
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var scanner = scope.ServiceProvider.GetRequiredService<IMediaScannerService>();
                var result = await scanner.ScanFoldersAsync(folders, progress);
                await hub.Clients.All.SendAsync("ScanCompleted", result);
            }
            catch (Exception ex)
            {
                await hub.Clients.All.SendAsync("ScanError", new { error = ex.Message });
            }
        });

        return Accepted(new { message = "Scan started. Connect to /hubs/scan for progress." });
    }
    public record AutoClassifyResult(int Reclassified, int Skipped, int Total);

    /// <summary>
    /// Auto-classify all media items using the scoring engine.
    /// Items confidently detected as Series or Education are updated (type +
    /// SeriesName / SeasonNumber / EpisodeNumber). Unrecognised items are left alone.
    /// </summary>
    /// <response code="200">Classification summary</response>
    [HttpPost("auto-classify")]
    [ProducesResponseType<AutoClassifyResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AutoClassify()
    {
        // Load all items (no pagination — classification is a background op)
        var all = await repo.GetAllAsync(1, int.MaxValue, null, null, null, false);
        int reclassified = 0, skipped = 0;

        foreach (var item in all)
        {
            var filename  = Path.GetFileName(item.FilePath);
            var folder    = Path.GetFileName(Path.GetDirectoryName(item.FilePath) ?? "");
            var (type, seriesScore, courseScore) = MediaScannerService.ScoreFilename(filename, folder);
            var maxScore  = Math.Max(seriesScore, courseScore);

            // Only re-classify if the engine is confident (score ≥ 2) and the
            // result differs from Movie (i.e. it actually found series/course signals).
            // Never override Documentary, MusicVideo — those are manually assigned.
            bool isManual = item.Type is MediaType.Documentary or MediaType.MusicVideo;
            if (isManual || maxScore < 2) { skipped++; continue; }

            var parsed = MediaScannerService.ParseFilename(filename, folder);

            item.Type          = type;
            item.SeriesName    = parsed.SeriesName ?? item.SeriesName;
            item.SeasonNumber  = parsed.Season     ?? item.SeasonNumber;
            item.EpisodeNumber = parsed.Episode    ?? parsed.LessonNumber ?? item.EpisodeNumber;

            await repo.UpdateAsync(item);
            reclassified++;
        }

        return Ok(new AutoClassifyResult(reclassified, skipped, reclassified + skipped));
    }
    public record MediaGroup(
        string Name,
        string? PosterPath,
        MediaType Type,
        int Count,
        int? Year);

    /// <summary>
    /// Return grouped Series/Education items — one entry per unique SeriesName.
    /// Used to render the series/course shelf where you see one card per show.
    /// </summary>
    /// <response code="200">List of groups</response>
    [HttpGet("groups")]
    [ProducesResponseType<IEnumerable<MediaGroup>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGroups([FromQuery] MediaType? type = null)
    {
        var items = await repo.GetAllAsync(
            1, int.MaxValue, type, null, null, false, "title", false);

        var groups = items
            .Where(i => !string.IsNullOrWhiteSpace(i.SeriesName))
            .GroupBy(i => i.SeriesName!)
            .Select(g => new MediaGroup(
                g.Key,
                g.FirstOrDefault(x => x.PosterPath != null)?.PosterPath,
                g.First().Type,
                g.Count(),
                g.Min(x => x.Year)))
            .OrderBy(g => g.Name)
            .ToList();

        return Ok(groups);
    }

    /// <summary>
    /// Return all episodes/lessons belonging to a specific series/course name.
    /// </summary>
    /// <response code="200">List of episodes/lessons</response>
    [HttpGet("series/{seriesName}")]
    [ProducesResponseType<IEnumerable<MediaItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSeriesEpisodes(string seriesName)
    {
        var all = await repo.GetAllAsync(
            1, int.MaxValue, null, null, null, false, "title", false);
        var episodes = all
            .Where(i => string.Equals(i.SeriesName, seriesName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.SeasonNumber ?? 0)
            .ThenBy(i => i.EpisodeNumber ?? 0)
            .ThenBy(i => i.Title)
            .ToList();
        return Ok(episodes);
    }

}