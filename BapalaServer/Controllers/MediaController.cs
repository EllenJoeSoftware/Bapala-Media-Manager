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
}
