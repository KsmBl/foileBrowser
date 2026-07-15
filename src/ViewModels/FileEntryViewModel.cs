using FoileBrowser.Models;

namespace FoileBrowser.ViewModels;

/// <summary>
/// Display wrapper around a <see cref="FileSystemEntry"/> for binding in the file list.
/// Immutable — a new instance is created per enumeration snapshot.
/// </summary>
public sealed class FileEntryViewModel(FileSystemEntry entry, string? location = null, string? tagColor = null)
{
    public FileSystemEntry Entry { get; } = entry;

    /// <summary>Containing directory, shown under the name for flattened search hits (PRD §6.4). Null when browsing.</summary>
    public string? LocationDisplay { get; } = location;

    public bool HasLocation => !string.IsNullOrEmpty(LocationDisplay);

    /// <summary>Color-tag hex for this entry, or null if untagged (PRD §6.7).</summary>
    public string? TagColor { get; } = tagColor;

    public bool HasTag => !string.IsNullOrEmpty(TagColor);

    public string Name => Entry.Name;

    public string FullPath => Entry.FullPath;

    public bool IsDirectory => Entry.IsDirectory;

    public bool IsHidden => Entry.IsHidden;

    /// <summary>A leading glyph hint; the real icon theming lands in a later milestone (PRD §6.7).</summary>
    public string Glyph => Entry.Kind switch
    {
        FileSystemEntryKind.Drive => "\U0001F5B4",     // 🖴 hard disk
        FileSystemEntryKind.Directory => "\U0001F4C1",  // 📁 folder
        _ => "\U0001F4C4",                              // 📄 page
    };

    public string SizeDisplay => Entry.Size is { } bytes ? FormatSize(bytes) : string.Empty;

    public string TypeDisplay => Entry switch
    {
        { Kind: FileSystemEntryKind.Drive } => "Drive",
        { IsDirectory: true } => "Folder",
        { Extension: "" } => "File",
        var e => e.Extension.ToUpperInvariant() + " file",
    };

    public string ModifiedDisplay =>
        Entry.Modified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    internal static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {Units[unit]}";
    }
}
