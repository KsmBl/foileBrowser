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

    /// <summary>Absolute path of an image to render, for image previews.</summary>
    public string? ImagePath { get; init; }

    public bool HasText => !string.IsNullOrEmpty(Text);
    public bool HasImage => !string.IsNullOrEmpty(ImagePath);
}
