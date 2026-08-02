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

    /// <summary>
    /// Which of <see cref="ImagePaths"/> to open on.
    /// </summary>
    /// <remarks>
    /// Not always the first. Picking one photograph out of a folder shows that photograph, with the
    /// rest of the folder alongside it in the strip — so the list starts at the top of the folder and
    /// the picture that was actually clicked is somewhere in the middle of it.
    /// </remarks>
    public int StartIndex { get; init; }

    /// <summary>The image a single-file preview is of — the one that was asked for.</summary>
    public string? ImagePath =>
        ImagePaths.Count > 0 ? ImagePaths[Math.Clamp(StartIndex, 0, ImagePaths.Count - 1)] : null;

    public bool HasText => !string.IsNullOrEmpty(Text);
    public bool HasImage => ImagePaths.Count > 0;

    /// <summary>Whether stepping and a thumbnail strip are worth showing.</summary>
    public bool HasManyImages => ImagePaths.Count > 1;
}
