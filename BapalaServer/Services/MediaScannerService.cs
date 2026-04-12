using System.Text.RegularExpressions;
using BapalaServer.Models;
using BapalaServer.Repositories;

namespace BapalaServer.Services;

public record ParsedFilename(string Title, int? Year, int? Season, int? Episode);

public class MediaScannerService(IMediaRepository repo, ITmdbService tmdb) : IMediaScannerService
{
    private static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".webm", ".flv"];

    public static bool IsVideoFile(string filename) =>
        VideoExtensions.Contains(Path.GetExtension(filename).ToLowerInvariant());

    public static MediaType DetectType(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(filename);
        return Regex.IsMatch(name, @"[Ss]\d{1,2}[Ee]\d{1,2}") ? MediaType.Series : MediaType.Movie;
    }

    public static ParsedFilename ParseFilename(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(filename);

        // Extract season/episode before stripping other info
        int? season = null, episode = null;
        var seMatch = Regex.Match(name, @"[Ss](\d{1,2})[Ee](\d{1,2})");
        if (seMatch.Success)
        {
            season = int.Parse(seMatch.Groups[1].Value);
            episode = int.Parse(seMatch.Groups[2].Value);
            name = name[..seMatch.Index].Trim();
        }

        // Strip quality tags and everything after
        name = Regex.Replace(name,
            @"\b(1080p|720p|480p|2160p|4K|UHD|BluRay|BRRip|WEB-DL|WEBRip|HDTV|DVDRip|x264|x265|HEVC|AAC|AC3|DTS|H\.264)\b.*",
            "", RegexOptions.IgnoreCase).Trim();

        // Extract year in parens or bare
        int? year = null;
        var yearMatch = Regex.Match(name, @"[\(\[](19|20)\d{2}[\)\]]|\b(19|20)\d{2}\b");
        if (yearMatch.Success)
        {
            year = int.Parse(Regex.Match(yearMatch.Value, @"\d{4}").Value);
            name = name.Replace(yearMatch.Value, "").Trim();
        }

        // Replace dots/underscores with spaces, collapse runs
        name = Regex.Replace(name, @"[._]", " ");
        name = Regex.Replace(name, @"\s{2,}", " ").Trim();

        return new ParsedFilename(name, year, season, episode);
    }

    public async Task<ScanResult> ScanFoldersAsync(
        IEnumerable<string> folders,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var files = folders
            .Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories))
            .Where(IsVideoFile)
            .ToList();

        int added = 0, updated = 0, skipped = 0;
        var errors = new List<string>();

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            progress?.Report(new ScanProgress(file, i + 1, files.Count));

            try
            {
                var existing = await repo.GetByFilePathAsync(file);
                if (existing != null) { skipped++; continue; }

                var parsed = ParseFilename(Path.GetFileName(file));
                var type = DetectType(file);

                var item = new MediaItem
                {
                    Title = parsed.Title,
                    Year = parsed.Year,
                    Type = type,
                    FilePath = file,
                    DateAdded = DateTime.UtcNow
                };

                // Fetch TMDB metadata — best-effort, never blocks the scan
                try
                {
                    var meta = await tmdb.FetchMetadataAsync(parsed.Title, parsed.Year, type, ct);
                    if (meta != null)
                    {
                        item.Description = meta.Description;
                        item.Genres = meta.Genres;
                        item.Rating = meta.Rating;
                        item.TmdbId = meta.TmdbId;
                        item.PosterPath = meta.PosterPath;
                        item.BackdropPath = meta.BackdropPath;
                    }
                }
                catch { /* metadata failure is non-critical */ }

                await repo.AddAsync(item);
                added++;
            }
            catch (Exception ex)
            {
                errors.Add($"{file}: {ex.Message}");
            }
        }

        return new ScanResult(added, updated, skipped, errors);
    }
}
