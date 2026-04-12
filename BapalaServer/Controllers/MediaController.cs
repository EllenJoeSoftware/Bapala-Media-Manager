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
    IMediaScannerService scanner,
    IHubContext<ScanProgressHub> hub,
    IConfiguration config) : ControllerBase
{
    public record MediaListResponse(IEnumerable<MediaItem> Items, int Total, int Page, int Limit);
    public record UpdateMediaRequest(string? Title, int? Year, string? Description, string? Genres, double? Rating);
    public record WatchProgressRequest(long ProgressSeconds);

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] MediaType? type = null,
        [FromQuery] string? genre = null,
        [FromQuery] string? search = null,
        [FromQuery] bool favorites = false)
    {
        var items = await repo.GetAllAsync(page, limit, type, genre, search, favorites);
        var total = await repo.CountAsync(type, genre, search, favorites);
        return Ok(new MediaListResponse(items, total, page, limit));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await repo.GetByIdAsync(id);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateMediaRequest req)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        if (req.Title != null) item.Title = req.Title;
        if (req.Year.HasValue) item.Year = req.Year;
        if (req.Description != null) item.Description = req.Description;
        if (req.Genres != null) item.Genres = req.Genres;
        if (req.Rating.HasValue) item.Rating = req.Rating;
        await repo.UpdateAsync(item);
        return Ok(item);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        await repo.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("{id}/favorite")]
    public async Task<IActionResult> ToggleFavorite(int id)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        item.IsFavorite = !item.IsFavorite;
        await repo.UpdateAsync(item);
        return Ok(new { item.IsFavorite });
    }

    [HttpPost("{id}/progress")]
    public async Task<IActionResult> SaveProgress(int id, [FromBody] WatchProgressRequest req)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound();
        await repo.UpsertWatchHistoryAsync(id, req.ProgressSeconds);
        return NoContent();
    }

    [HttpGet("{id}/progress")]
    public async Task<IActionResult> GetProgress(int id)
    {
        var history = await repo.GetWatchHistoryAsync(id);
        return Ok(new { ProgressSeconds = history?.ProgressSeconds ?? 0 });
    }

    [HttpPost("scan")]
    public IActionResult TriggerScan()
    {
        var foldersRaw = config["Bapala:MediaFolders"] ?? "";
        var folders = foldersRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (folders.Length == 0)
            return BadRequest(new { error = "No media folders configured. Add them in Settings." });

        _ = Task.Run(async () =>
        {
            await hub.Clients.All.SendAsync("ScanStarted", new { folders });
            var progress = new Progress<ScanProgress>(p =>
                hub.Clients.All.SendAsync("ScanProgress", p));
            try
            {
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
