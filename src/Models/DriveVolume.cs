namespace FoileBrowser.Models;

/// <summary>A mounted drive/volume with capacity info for the sidebar (PRD §6.1, §6.10).</summary>
public sealed record DriveVolume
{
    public required string Label { get; init; }
    public required string RootPath { get; init; }
    public long? FreeBytes { get; init; }
    public long? TotalBytes { get; init; }
    public string? FileSystem { get; init; }
}
