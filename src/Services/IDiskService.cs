namespace FoileBrowser.Services;

/// <summary>A filesystem foileBrowser can create, plus the mkfs invocation that makes it (PRD §6.10).</summary>
public sealed record FilesystemType(string Id, string Display, string MkfsCommand, string? LabelFlag, string[] ExtraArgs);

/// <summary>Outcome of a format operation: whether it succeeded and a human-readable message.</summary>
public sealed record FormatResult(bool Success, string Message);

/// <summary>
/// Creates filesystems on block devices (partitions/disks). Destructive — callers must confirm with
/// the user first. On Linux the work runs as root via <c>pkexec</c> (polkit) so no app-level privilege
/// is needed; unsupported platforms report no available filesystems (PRD §6.10).
/// </summary>
public interface IDiskService
{
    /// <summary>The filesystem types whose mkfs tools are installed on this machine.</summary>
    IReadOnlyList<FilesystemType> AvailableFilesystems();

    /// <summary>
    /// Unmounts <paramref name="device"/> and creates a fresh <paramref name="fsId"/> filesystem on it
    /// (optionally labelled). Refuses to touch the running root device. Erases all data on the device.
    /// </summary>
    Task<FormatResult> FormatAsync(string device, string fsId, string? label, CancellationToken cancellationToken = default);
}
