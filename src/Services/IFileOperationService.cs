using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>
/// Filesystem mutations behind the copy/move queue (PRD §6.3). All work is async and
/// reports byte progress; name collisions are delegated to a resolver callback.
/// </summary>
public interface IFileOperationService
{
    /// <summary>Creates a new folder under <paramref name="parentDir"/>, returning its full path.</summary>
    Task<string> CreateFolderAsync(string parentDir, string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a new empty file under <paramref name="parentDir"/>, returning its full path.</summary>
    Task<string> CreateFileAsync(string parentDir, string name, CancellationToken cancellationToken = default);

    /// <summary>Renames a single entry in place, returning the new full path.</summary>
    Task<string> RenameAsync(string path, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies or moves <paramref name="sources"/> into <paramref name="destinationDir"/>.
    /// <paramref name="conflictResolver"/> is consulted per colliding entry; returning
    /// <see cref="ConflictResolution.Cancel"/> aborts the whole transfer.
    /// </summary>
    Task TransferAsync(
        IReadOnlyList<string> sources,
        string destinationDir,
        FileOperationKind kind,
        IProgress<OperationProgress>? progress,
        Func<ConflictRequest, ConflictResolution> conflictResolver,
        CancellationToken cancellationToken = default);
}
