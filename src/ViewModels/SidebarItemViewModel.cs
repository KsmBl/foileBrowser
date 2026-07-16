using System.Windows.Input;
using FoileBrowser.Models;

namespace FoileBrowser.ViewModels;

public enum SidebarItemKind
{
    Header,
    Favorite,
    Drive,
    Device,
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

    /// <summary>Filesystem type label (e.g. "ext4", "MTP"), shown for devices (PRD §6.10).</summary>
    public string? FileSystem { get; init; }

    /// <summary>True for removable/GVfs volumes that can be ejected (PRD §6.10).</summary>
    public bool IsEjectable { get; init; }

    /// <summary>Set by the shell for user-pinned favorites so they can be unpinned via the context menu.</summary>
    public ICommand? UnpinCommand { get; init; }

    public bool CanUnpin => UnpinCommand is not null;

    public bool IsNavigable => Kind is SidebarItemKind.Favorite or SidebarItemKind.Drive or SidebarItemKind.Device;

    public bool IsHeader => Kind is SidebarItemKind.Header;

    public bool HasCapacity => TotalBytes is > 0;

    public bool HasFileSystem => !string.IsNullOrEmpty(FileSystem);

    public string Glyph => Kind switch
    {
        SidebarItemKind.Drive => "\U0001F5B4",     // 🖴
        SidebarItemKind.Device => "\U0001F4F1",    // 📱 removable / phone
        SidebarItemKind.Favorite => "\U0001F4CC",  // 📌
        _ => string.Empty,
    };

    public double UsedFraction =>
        TotalBytes is > 0 && FreeBytes is >= 0 ? Math.Clamp(1.0 - (double)FreeBytes.Value / TotalBytes.Value, 0, 1) : 0;

    public string FreeSpaceDisplay =>
        FreeBytes is { } free && TotalBytes is { } total
            ? $"{ValueFormat.Size(free, SizeUnit.Binary)} free of {ValueFormat.Size(total, SizeUnit.Binary)}"
            : string.Empty;
}
