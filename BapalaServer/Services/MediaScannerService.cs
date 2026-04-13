using System.Text.RegularExpressions;
using BapalaServer.Models;
using BapalaServer.Repositories;

namespace BapalaServer.Services;

public record ParsedFilename(
    string Title,
    int? Year,
    int? Season,
    int? Episode,
    string? SeriesName,
    int? LessonNumber);

// ── Regex pattern sets (mirrors the JS patterns the user provided) ────────────

public static class MediaPatterns
{
    // Series
    public static readonly Regex[] SeriesPatterns =
    [
        new(@"S(\d{1,2})E(\d{1,2})", RegexOptions.IgnoreCase),
        new(@"(\d{1,2})x(\d{1,2})", RegexOptions.IgnoreCase),
        new(@"Season[ ._-]?(\d{1,2})", RegexOptions.IgnoreCase),
        new(@"Episode[ ._-]?(\d{1,3})", RegexOptions.IgnoreCase),
        new(@"Ep[ ._-]?(\d{1,3})", RegexOptions.IgnoreCase),
    ];

    // Course
    public static readonly Regex[] CoursePatterns =
    [
        new(@"Lesson[ ._-]?(\d{1,3})", RegexOptions.IgnoreCase),
        new(@"Module[ ._-]?(\d{1,3})", RegexOptions.IgnoreCase),
        new(@"Chapter[ ._-]?(\d{1,3})", RegexOptions.IgnoreCase),
        new(@"Part[ ._-]?(\d{1,3})", RegexOptions.IgnoreCase),
        new(@"Lecture[ ._-]?(\d{1,3})", RegexOptions.IgnoreCase),
        new(@"Unit[ ._-]?(\d{1,3})", RegexOptions.IgnoreCase),
        // Leading zero-padded track number: "003 Title", "01 - Intro", "1. Overview"
        new(@"^(\d{1,3})[ ._-]"),
    ];

    // Movie fallback
    public static readonly Regex[] MoviePatterns =
    [
        new(@"\b(19|20)\d{2}\b"),
    ];

    // Folder-level detection
    public static readonly Regex FolderSeriesRx = new(@"Season", RegexOptions.IgnoreCase);
    public static readonly Regex FolderCourseRx = new(@"Module|Course|Lesson", RegexOptions.IgnoreCase);

    // Strong bonus signals
    public static readonly Regex StrongSeriesRx = new(@"S\d{1,2}.*E\d{1,2}", RegexOptions.IgnoreCase);
    public static readonly Regex StrongCourseRx = new(@"Lesson|Module|^\d{1,3}[ ._-]", RegexOptions.IgnoreCase);

    // Cleanup
    public static readonly Regex CleanRx = new(@"[._-]");
}

public class MediaScannerService(IMediaRepository repo, ITmdbService tmdb) : IMediaScannerService
{
    private static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".webm", ".flv"];

    public static bool IsVideoFile(string filename) =>
        VideoExtensions.Contains(Path.GetExtension(filename).ToLowerInvariant());

    // ── Core scoring engine ───────────────────────────────────────────────────

    public static (MediaType type, int seriesScore, int courseScore) ScoreFilename(
        string filename, string folderName = "")
    {
        var name = MediaPatterns.CleanRx.Replace(
            Path.GetFileNameWithoutExtension(filename), " ");

        int seriesScore = 0, courseScore = 0, movieScore = 0;

        foreach (var p in MediaPatterns.SeriesPatterns)
            if (p.IsMatch(name)) seriesScore += 2;
        foreach (var p in MediaPatterns.CoursePatterns)
            if (p.IsMatch(name)) courseScore += 2;
        foreach (var p in MediaPatterns.MoviePatterns)
            if (p.IsMatch(name)) movieScore += 1;

        if (MediaPatterns.StrongSeriesRx.IsMatch(name)) seriesScore += 3;
        if (MediaPatterns.StrongCourseRx.IsMatch(name)) courseScore += 3;

        // Folder-level bonus
        if (MediaPatterns.FolderSeriesRx.IsMatch(folderName)) seriesScore += 4;
        if (MediaPatterns.FolderCourseRx.IsMatch(folderName)) courseScore += 4;

        int max = Math.Max(seriesScore, Math.Max(courseScore, movieScore));

        if (max == 0) return (MediaType.Movie, seriesScore, courseScore);
        if (seriesScore == max) return (MediaType.Series, seriesScore, courseScore);
        if (courseScore == max) return (MediaType.Education, seriesScore, courseScore);
        return (MediaType.Movie, seriesScore, courseScore);
    }

    public static MediaType DetectType(string filename, string folderName = "") =>
        ScoreFilename(filename, folderName).type;

    // ── Parse filename into structured data ───────────────────────────────────

    public static ParsedFilename ParseFilename(string filename, string folderName = "")
    {
        var raw = Path.GetFileNameWithoutExtension(filename);
        var name = raw;

        // ── Extract season/episode (S01E01 or 1x01) ──────────────────────────
        int? season = null, episode = null;
        string? seriesName = null;

        var seMatch = Regex.Match(name, @"[Ss](\d{1,2})[Ee](\d{1,2})");
        if (seMatch.Success)
        {
            season  = int.Parse(seMatch.Groups[1].Value);
            episode = int.Parse(seMatch.Groups[2].Value);
            // Everything before the SxxExx tag is the series name
            seriesName = CleanTitle(name[..seMatch.Index]);
            name = name[(seMatch.Index + seMatch.Length)..].Trim();
        }
        else
        {
            var nxMatch = Regex.Match(name, @"(\d{1,2})x(\d{1,2})");
            if (nxMatch.Success)
            {
                season  = int.Parse(nxMatch.Groups[1].Value);
                episode = int.Parse(nxMatch.Groups[2].Value);
                seriesName = CleanTitle(name[..nxMatch.Index]);
                name = name[(nxMatch.Index + nxMatch.Length)..].Trim();
            }
        }

        // ── Extract lesson/module/chapter number ──────────────────────────────
        int? lessonNumber = null;
        // Named keyword patterns: "Lesson 01", "Module 3", "Part 1 - ..."
        var lessonMatch = Regex.Match(raw,
            @"(Lesson|Module|Chapter|Part|Lecture|Unit)[ ._-]?(\d{1,3})",
            RegexOptions.IgnoreCase);
        if (lessonMatch.Success)
        {
            lessonNumber = int.Parse(lessonMatch.Groups[2].Value);
            // Series name = everything before the lesson keyword
            if (seriesName == null)
            {
                var beforeLesson = raw[..lessonMatch.Index];
                var cleaned = CleanTitle(beforeLesson);
                if (!string.IsNullOrWhiteSpace(cleaned))
                    seriesName = cleaned;
            }
        }

        // Leading zero-padded track number: "003 Process Injection - Part 1 - Explanation of APIs"
        // Only apply if no other lesson number was found yet.
        if (lessonNumber == null)
        {
            var leadMatch = Regex.Match(raw, @"^(\d{1,3})[ ._-]");
            if (leadMatch.Success)
            {
                lessonNumber = int.Parse(leadMatch.Groups[1].Value);
                // Strip the number prefix from the title — everything after the separator
                if (seriesName == null)
                {
                    // The remainder after "003 " becomes the episode title;
                    // seriesName comes from the containing folder name (set below).
                    name = raw[(leadMatch.Index + leadMatch.Length)..].Trim();
                }
            }
        }

        // ── If series name still null but folder says "Season X", use folder parent ─
        if (seriesName == null && MediaPatterns.FolderSeriesRx.IsMatch(folderName))
        {
            // e.g. folder = "Season 1" — parent folder name is the show name
            // We can't know parent here, but we store the season from folder name
            var folderSeasonMatch = Regex.Match(folderName, @"Season[ ._-]?(\d{1,2})", RegexOptions.IgnoreCase);
            if (folderSeasonMatch.Success && season == null)
                season = int.Parse(folderSeasonMatch.Groups[1].Value);
        }

        // ── Strip quality tags ────────────────────────────────────────────────
        name = Regex.Replace(name,
            @"\b(1080p|720p|480p|2160p|4K|UHD|BluRay|BRRip|WEB-DL|WEBRip|HDTV|DVDRip|x264|x265|HEVC|AAC|AC3|DTS|H\.264)\b.*",
            "", RegexOptions.IgnoreCase).Trim();

        // ── Extract year ──────────────────────────────────────────────────────
        int? year = null;
        var yearMatch = Regex.Match(name, @"[\(\[](19|20)\d{2}[\)\]]|\b(19|20)\d{2}\b");
        if (yearMatch.Success)
        {
            year = int.Parse(Regex.Match(yearMatch.Value, @"\d{4}").Value);
            name = name.Replace(yearMatch.Value, "").Trim();
        }

        // ── Final title cleanup ───────────────────────────────────────────────
        var title = CleanTitle(string.IsNullOrWhiteSpace(name) ? raw : name);
        if (string.IsNullOrWhiteSpace(seriesName)) seriesName = null;

        return new ParsedFilename(title, year, season, episode, seriesName, lessonNumber);
    }

    private static string CleanTitle(string s)
    {
        s = Regex.Replace(s, @"[._-]", " ");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();
        return s;
    }

    // ── Auto-classify existing library items ──────────────────────────────────

    public static (MediaType type, string? seriesName, int? season, int? episode, int? lesson)
        ClassifyItem(MediaItem item)
    {
        var filename = Path.GetFileName(item.FilePath);
        var folderName = Path.GetFileName(Path.GetDirectoryName(item.FilePath) ?? "");
        var (type, _, _) = ScoreFilename(filename, folderName);
        var parsed = ParseFilename(filename, folderName);
        return (type, parsed.SeriesName, parsed.Season, parsed.Episode, parsed.LessonNumber);
    }

    // ── Full library scan ─────────────────────────────────────────────────────

    public async Task<ScanResult> ScanFoldersAsync(
        IEnumerable<string> folders,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        var files  = new List<string>();

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            { errors.Add($"Folder not found: '{folder}'"); continue; }
            try
            {
                files.AddRange(
                    Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                             .Where(IsVideoFile));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                errors.Add($"Cannot access folder '{folder}': {ex.Message}");
            }
        }

        int added = 0, updated = 0, skipped = 0;

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            progress?.Report(new ScanProgress(file, i + 1, files.Count));

            try
            {
                var existing = await repo.GetByFilePathAsync(file);
                if (existing != null) { skipped++; continue; }

                var folderName = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
                var parsed = ParseFilename(Path.GetFileName(file), folderName);
                var type   = DetectType(file, folderName);

                var item = new MediaItem
                {
                    Title         = parsed.Title,
                    Year          = parsed.Year,
                    Type          = type,
                    FilePath      = file,
                    DateAdded     = DateTime.UtcNow,
                    SeriesName    = parsed.SeriesName,
                    SeasonNumber  = parsed.Season,
                    EpisodeNumber = parsed.Episode ?? parsed.LessonNumber,
                };

                try
                {
                    using var tmdbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    tmdbCts.CancelAfter(TimeSpan.FromSeconds(5));
                    var meta = await tmdb.FetchMetadataAsync(
                        parsed.SeriesName ?? parsed.Title, parsed.Year, type, tmdbCts.Token);
                    if (meta != null)
                    {
                        item.Description  = meta.Description;
                        item.Genres       = meta.Genres;
                        item.Rating       = meta.Rating;
                        item.TmdbId       = meta.TmdbId;
                        item.PosterPath   = meta.PosterPath;
                        item.BackdropPath = meta.BackdropPath;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException { CancellationToken.IsCancellationRequested: true }
                                            || !ct.IsCancellationRequested)
                {
                    errors.Add($"[TMDB] {Path.GetFileName(file)}: {ex.Message}");
                }

                await repo.AddAsync(item);
                added++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{file}: {ex.Message}");
            }
        }

        return new ScanResult(added, updated, skipped, errors);
    }
}
