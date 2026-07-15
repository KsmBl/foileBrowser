using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>
/// Recursive fuzzy search across a directory tree, streaming hits as they are found so the
/// UI can show results progressively and cancel mid-scan (PRD §6.4).
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Walks <paramref name="rootPath"/> depth-first, yielding entries whose name fuzzy-matches
    /// <paramref name="query"/>. When <paramref name="extensions"/> is non-empty only files with a
    /// matching (dot-less, lower-case) extension are considered. Unreadable directories are skipped.
    /// </summary>
    IAsyncEnumerable<FileSystemEntry> SearchAsync(
        string rootPath,
        string query,
        IReadOnlyCollection<string>? extensions = null,
        CancellationToken cancellationToken = default);
}
