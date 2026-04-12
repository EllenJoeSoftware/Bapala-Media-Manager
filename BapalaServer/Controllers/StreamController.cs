using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BapalaServer.Repositories;

namespace BapalaServer.Controllers;

[ApiController]
[Route("api/stream")]
[Authorize]
public class StreamController(IMediaRepository repo, IConfiguration config) : ControllerBase
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"]  = "video/mp4",
        [".mkv"]  = "video/x-matroska",
        [".avi"]  = "video/x-msvideo",
        [".mov"]  = "video/quicktime",
        [".wmv"]  = "video/x-ms-wmv",
        [".m4v"]  = "video/x-m4v",
        [".webm"] = "video/webm",
        [".ts"]   = "video/mp2t",
        [".flv"]  = "video/x-flv",
    };

    public static string GetMimeType(string extension) =>
        MimeTypes.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";

    /// <summary>
    /// Streams a video file with HTTP 206 Partial Content support for seeking.
    /// JWT passed as ?token= query param — HTML video elements can't set headers.
    /// PhysicalFile with enableRangeProcessing=true handles Range headers automatically.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Stream(int id, CancellationToken ct)
    {
        var item = await repo.GetByIdAsync(id);
        if (item == null) return NotFound(new { error = "Media not found" });

        if (!System.IO.File.Exists(item.FilePath))
            return NotFound(new { error = "File not found on disk" });

        // Confine streaming to configured media folders to prevent path traversal
        var foldersRaw = config["Bapala:MediaFolders"] ?? "";
        var allowedFolders = foldersRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => Path.GetFullPath(f))
            .ToList();
        var fullPath = Path.GetFullPath(item.FilePath);
        if (allowedFolders.Count > 0 &&
            !allowedFolders.Any(f => fullPath.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
        {
            return Forbid();
        }

        var mime = GetMimeType(Path.GetExtension(item.FilePath));
        return PhysicalFile(item.FilePath, mime, enableRangeProcessing: true);
    }
}
