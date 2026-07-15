using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>
/// Reads the filesystem off the UI thread. Every member is async and is expected to
/// swallow per-entry I/O errors so the UI never blocks or crashes on unreadable media
/// (PRD §6.12: "All I/O async; UI thread never blocks").
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// Enumerates the immediate children of <paramref name="path"/>. Entries that cannot be
    /// stat-ed are still returned with whatever metadata was available. Throws only for the
    /// directory itself being unreadable/missing; individual entry errors are absorbed.
    /// </summary>
    Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(
        string path, CancellationToken cancellationToken = default);

    /// <summary>Lists the machine's drive/volume roots (PRD §6.1 drive list).</summary>
    Task<IReadOnlyList<FileSystemEntry>> ListDrivesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Lists mounted volumes with capacity/filesystem info for the sidebar (PRD §6.1, §6.10).</summary>
    Task<IReadOnlyList<DriveVolume>> ListVolumesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the parent directory of <paramref name="path"/>, or null if it is already a root.
    /// </summary>
    string? GetParent(string path);

    bool DirectoryExists(string path);
}
