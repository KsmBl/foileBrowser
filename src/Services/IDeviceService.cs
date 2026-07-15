namespace FoileBrowser.Services;

/// <summary>Mount/unmount/eject of removable media and GVfs mounts (PRD §6.10).</summary>
public interface IDeviceService
{
    /// <summary>Safely unmounts/ejects the volume at <paramref name="mountPath"/>. Best-effort per platform.</summary>
    Task EjectAsync(string mountPath, CancellationToken cancellationToken = default);
}
