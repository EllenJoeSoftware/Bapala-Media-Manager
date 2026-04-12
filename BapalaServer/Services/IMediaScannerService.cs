namespace BapalaServer.Services;

public interface IMediaScannerService
{
    Task<ScanResult> ScanFoldersAsync(
        IEnumerable<string> folders,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default);
}

public record ScanResult(int Added, int Updated, int Skipped, IReadOnlyList<string> Errors);
public record ScanProgress(string CurrentFile, int Processed, int Total);
