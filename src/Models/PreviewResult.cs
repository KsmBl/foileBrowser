namespace FoileBrowser.Models;

public enum PreviewKind
{
    None,
    Text,
    Image,
    Folder,
}

/// <summary>
/// The result of inspecting a single entry for the inspector panel / quick preview (PRD §6.5).
/// Carries display-ready data only (no UI types) so the service stays testable.
/// </summary>
public sealed record PreviewResult
{
    public required PreviewKind Kind { get; init; }

    /// <summary>Header line: the entry name.</summary>
    public required string Title { get; init; }

    /// <summary>Secondary detail line (size · type · modified, or folder summary).</summary>
    public string Info { get; init; } = string.Empty;

    /// <summary>Text body for text/folder previews (truncated).</summary>
    public string? Text { get; init; }

    /// <summary>
    /// Absolute paths of the images to render, in the order they should be stepped through. One entry
    /// for a single file; a whole selection's worth when several were picked (PRD §6.5).
    /// </summary>
    public IReadOnlyList<string> ImagePaths { get; init; } = [];

    /// <summary>The first image, which is the only one a single-file preview has.</summary>
    public string? ImagePath => ImagePaths.Count > 0 ? ImagePaths[0] : null;

    public bool HasText => !string.IsNullOrEmpty(Text);
    public bool HasImage => ImagePaths.Count > 0;

    /// <summary>Whether stepping and a thumbnail strip are worth showing.</summary>
    public bool HasManyImages => ImagePaths.Count > 1;
}
