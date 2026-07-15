namespace FoileBrowser.Models;

/// <summary>One entry inside an archive (PRD §6.11).</summary>
public sealed record ArchiveEntry
{
    public required string Name { get; init; }
    public required bool IsDirectory { get; init; }
    public long Size { get; init; }
    public long CompressedSize { get; init; }
    public DateTimeOffset? Modified { get; init; }
}
