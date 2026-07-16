namespace FoileBrowser.Services;

/// <summary>
/// Computes recursive folder sizes on background threads and caches them in an in-memory LRU
/// (PRD §6.2 computed folder sizes). Results are shared process-wide so re-visiting a folder is
/// instant, and concurrent calculations are throttled so listing a folder full of subfolders does
/// not stampede the disk.
/// </summary>
public interface IDirectorySizeService
{
    /// <summary>Returns a previously computed size for <paramref name="path"/> without touching disk.</summary>
    bool TryGetCached(string path, out long size);

    /// <summary>
    /// Returns the recursive byte size of <paramref name="path"/>, computing and caching it if needed.
    /// <paramref name="progress"/> receives the running total as the walk proceeds (per-folder progress).
    /// </summary>
    Task<long> GetSizeAsync(string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}
