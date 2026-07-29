using System.Windows.Input;
using FoileBrowser.Models;

namespace FoileBrowser.ViewModels;

public enum SidebarItemKind
{
    Header,
    Favorite,
    Drive,
    Device,

    /// <summary>A physical disk grouping its partitions (a non-navigable label row).</summary>
    Disk,

    /// <summary>A partition shown indented under its disk.</summary>
    Partition,
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

    /// <summary>
    /// True for a removable volume that is present but not mounted (PRD §6.10). It has no path to
    /// browse yet; opening it mounts it first, so a click does the whole job.
    /// </summary>
    public bool NeedsMounting { get; init; }

    /// <summary>Set by the shell for user-pinned favorites so they can be unpinned via the context menu.</summary>
    public ICommand? UnpinCommand { get; init; }

    // Context-menu actions, injected by the shell so the popup (a separate visual tree) can reach them.
    public ICommand? OpenCommand { get; init; }
    public ICommand? OpenInNewTabCommand { get; init; }
    public ICommand? OpenInNewPaneCommand { get; init; }
    public ICommand? EjectCommand { get; init; }
    public ICommand? FormatCommand { get; init; }

    /// <summary>Backing block device (e.g. "/dev/sdb1") for drives/partitions; null for GVfs/favorites.</summary>
    public string? Device { get; init; }

    /// <summary>True when this row can be formatted (a real block device, formatting enabled — PRD §6.10).</summary>
    public bool CanFormat { get; init; }

    public bool CanUnpin => UnpinCommand is not null;

    /// <summary>True when the sidebar row has any context-menu action below "open" (keeps the separator tidy).</summary>
    public bool HasActions => IsEjectable || CanUnpin || CanFormat || NeedsMounting;

    public bool IsNavigable =>
        Kind is SidebarItemKind.Favorite or SidebarItemKind.Drive or SidebarItemKind.Device or SidebarItemKind.Partition;

    public bool IsHeader => Kind is SidebarItemKind.Header;

    /// <summary>A physical-disk grouping row (non-navigable label above its partitions).</summary>
    public bool IsDiskGroup => Kind is SidebarItemKind.Disk;

    /// <summary>Left indent (partitions sit under their disk).</summary>
    public double Indent => Kind is SidebarItemKind.Partition ? 14 : 0;

    /// <summary>Whether a free-space bar can be drawn. An unmounted device has a size but no free
    /// figure to fill the bar with, so it says "not mounted" instead.</summary>
    public bool HasCapacity => TotalBytes is > 0 && !NeedsMounting;

    public bool HasFileSystem => !string.IsNullOrEmpty(FileSystem);

    public string Glyph => Kind switch
    {
        SidebarItemKind.Disk => "\U0001F5B4",      // 🖴 physical disk
        SidebarItemKind.Drive => "\U0001F5B4",     // 🖴
        SidebarItemKind.Partition => "\U0001F9E9", // 🧩 partition
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
