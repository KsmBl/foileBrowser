namespace FoileBrowser.Models;

public enum VolumeKind
{
    Fixed,
    Removable,
    Gvfs,
}

/// <summary>A mounted drive/volume with capacity info for the sidebar (PRD §6.1, §6.10).</summary>
public sealed record DriveVolume
{
    public required string Label { get; init; }
    public required string RootPath { get; init; }
    public long? FreeBytes { get; init; }
    public long? TotalBytes { get; init; }
    public string? FileSystem { get; init; }
    public VolumeKind Kind { get; init; } = VolumeKind.Fixed;

    /// <summary>Backing block device, e.g. "/dev/sda1" (Linux). Null for GVfs and where unknown.</summary>
    public string? Device { get; init; }

    /// <summary>Physical disk the volume/partition lives on, e.g. "sda", "nvme0n1". Null where unknown.</summary>
    public string? Disk { get; init; }

    public bool IsRemovable => Kind is VolumeKind.Removable or VolumeKind.Gvfs;
}
