using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>Builds a preview for the inspector panel and spacebar quick-preview (PRD §6.5).</summary>
public interface IPreviewService
{
    Task<PreviewResult> CreateAsync(FileSystemEntry entry, CancellationToken cancellationToken = default);
}
