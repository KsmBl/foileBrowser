using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>
/// Reads archives (ZIP, TAR, 7z, RAR, CAB, …) through CompressionWorkbench (PRD §6.11).
/// </summary>
public interface IArchiveService
{
    /// <summary>True if <paramref name="path"/>'s extension maps to a listable archive format.</summary>
    bool IsArchive(string path);

    /// <summary>Human-readable format name for the file, or null if unrecognised (PRD §6.11 "what is this?").</summary>
    string? Identify(string path);

    /// <summary>Lists the entries of the archive at <paramref name="path"/>.</summary>
    Task<IReadOnlyList<ArchiveEntry>> ListAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts the whole archive into <paramref name="destinationDir"/>, reporting byte progress.
    /// </summary>
    Task ExtractAllAsync(
        string path, string destinationDir,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
}
