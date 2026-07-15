namespace FoileBrowser.Services;

/// <summary>
/// Sends files/folders to the OS trash rather than deleting permanently (PRD §6.3).
/// Implementations are platform-specific (Recycle Bin / gio trash / Finder trash).
/// </summary>
public interface ITrashService
{
    /// <summary>Moves <paramref name="path"/> to the OS trash. Throws if the platform trash is unavailable.</summary>
    Task TrashAsync(string path, CancellationToken cancellationToken = default);
}
