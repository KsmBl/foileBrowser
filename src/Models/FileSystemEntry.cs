namespace FoileBrowser.Models;

/// <summary>
/// An immutable snapshot of a single filesystem entry (file, directory, or drive root)
/// as read during a directory enumeration. Snapshots are cheap value-like records so
/// they can be produced off the UI thread and handed to the view model in bulk.
/// </summary>
public sealed record FileSystemEntry
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public required FileSystemEntryKind Kind { get; init; }

    /// <summary>Size in bytes. Null for directories (computed on demand later — PRD §6.2).</summary>
    public long? Size { get; init; }

    public DateTimeOffset? Modified { get; init; }

    /// <summary>True for hidden or system entries, used by the visibility toggle (PRD §6.1).</summary>
    public bool IsHidden { get; init; }

    public bool IsDirectory => Kind is FileSystemEntryKind.Directory or FileSystemEntryKind.Drive;

    /// <summary>Lower-case extension without the dot, or empty for directories/extension-less files.</summary>
    public string Extension =>
        IsDirectory ? string.Empty : Path.GetExtension(Name).TrimStart('.').ToLowerInvariant();
}

public enum FileSystemEntryKind
{
    File,
    Directory,
    Drive,
}
