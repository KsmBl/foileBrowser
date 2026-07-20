namespace FoileBrowser.Services;

/// <summary>Describes a metadata column the <see cref="IMetadataService"/> can fill (PRD §6.1).</summary>
public sealed record MetadataColumnInfo(string Id, string Header, string Category, bool RightAligned, double Width);

/// <summary>
/// Computes per-file metadata for the extra file-list columns (image dimensions/channels/colors via
/// SkiaSharp; audio/video fps/duration/channels/… via ffprobe when installed). Values are cached and
/// computed lazily on background threads, so only columns that are actually shown, for rows that are
/// actually on screen, ever touch a file (PRD §6.1).
/// </summary>
public interface IMetadataService
{
    /// <summary>The metadata columns this service provides (for the column catalogue).</summary>
    IReadOnlyList<MetadataColumnInfo> Columns { get; }

    /// <summary>
    /// The display value for <paramref name="columnId"/> of <paramref name="path"/>: a cached value,
    /// "" when the column doesn't apply to that file, or "…" while it computes in the background —
    /// in which case <paramref name="onReady"/> fires (once ready) so the row can refresh.
    /// </summary>
    string Get(string path, string columnId, Action onReady);
}
