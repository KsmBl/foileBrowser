namespace FoileBrowser.Services;

/// <summary>Mount/unmount/eject of removable media and GVfs mounts (PRD §6.10).</summary>
public interface IDeviceService
{
    /// <summary>Safely unmounts/ejects the volume at <paramref name="mountPath"/>. Best-effort per platform.</summary>
    Task EjectAsync(string mountPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mounts the block device <paramref name="device"/> (e.g. <c>/dev/sdb1</c>), returning where it
    /// landed, or null if it could not be mounted (PRD §6.10).
    /// </summary>
    Task<string?> MountAsync(string device, CancellationToken cancellationToken = default);
}
