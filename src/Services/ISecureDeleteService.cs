namespace FoileBrowser.Services;

/// <summary>
/// Permanently deletes files after overwriting their contents with zeroes (PRD §6.3).
///
/// This is a best-effort wipe, not a guarantee. Overwriting a file's bytes only reliably destroys the
/// old data on a traditional overwrite-in-place filesystem on rotating media. On SSDs (wear levelling,
/// TRIM, over-provisioning), copy-on-write filesystems (btrfs, ZFS), journalled or compressed
/// filesystems, RAID, and network or virtual storage, the original blocks may survive untouched.
/// Callers must say so before the user commits to it.
/// </summary>
public interface ISecureDeleteService
{
    /// <summary>
    /// Overwrites <paramref name="path"/> with zeroes and deletes it, recursing into directories.
    /// Reports bytes overwritten so far.
    /// </summary>
    Task ShredAsync(string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}
