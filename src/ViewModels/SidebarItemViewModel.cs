using FoileBrowser.Models;

namespace FoileBrowser.ViewModels;

public enum SidebarItemKind
{
    Header,
    Favorite,
    Drive,
}

/// <summary>An entry in the collapsible sidebar: a section header, pinned favorite, or drive (PRD §6.2).</summary>
public sealed class SidebarItemViewModel
{
    public required string Name { get; init; }

    /// <summary>Target path; empty for headers.</summary>
    public string Path { get; init; } = string.Empty;

    public required SidebarItemKind Kind { get; init; }

    /// <summary>Free space in bytes for drives, used by the sidebar free-space bar (PRD §6.10).</summary>
    public long? FreeBytes { get; init; }

    public long? TotalBytes { get; init; }

    public bool IsNavigable => Kind is SidebarItemKind.Favorite or SidebarItemKind.Drive;

    public bool IsHeader => Kind is SidebarItemKind.Header;

    public bool HasCapacity => TotalBytes is > 0;

    public string Glyph => Kind switch
    {
        SidebarItemKind.Drive => "\U0001F5B4",
        SidebarItemKind.Favorite => "\U0001F4CC", // 📌
        _ => string.Empty,
    };

    public double UsedFraction =>
        TotalBytes is > 0 && FreeBytes is >= 0 ? Math.Clamp(1.0 - (double)FreeBytes.Value / TotalBytes.Value, 0, 1) : 0;

    public string FreeSpaceDisplay =>
        FreeBytes is { } free && TotalBytes is { } total
            ? $"{FileEntryViewModel.FormatSize(free)} free of {FileEntryViewModel.FormatSize(total)}"
            : string.Empty;
}
