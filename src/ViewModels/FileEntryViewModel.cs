using CommunityToolkit.Mvvm.ComponentModel;
using FoileBrowser.Models;

namespace FoileBrowser.ViewModels;

/// <summary>
/// Display wrapper around a <see cref="FileSystemEntry"/> for binding in the file list.
/// A new instance is created per enumeration snapshot; only the background-computed folder
/// size is mutable/observable (PRD §6.2).
/// </summary>
public sealed partial class FileEntryViewModel(
    FileSystemEntry entry, string? location = null, string? tagColor = null, DisplayOptions? display = null)
    : ObservableObject
{
    private readonly DisplayOptions _display = display ?? new DisplayOptions();

    public FileSystemEntry Entry { get; } = entry;

    /// <summary>Recursively computed folder size, filled in on a background thread (PRD §6.2). Null until known.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeDisplay))]
    private long? _computedSize;

    /// <summary>True while the folder size is being calculated; shows a "counting" hint on the size.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeDisplay))]
    private bool _isCalculatingSize;

    /// <summary>Re-renders size/date after a display-mode toggle without rebuilding the list (PRD §6.1/§6.2).</summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(SizeDisplay));
        OnPropertyChanged(nameof(ModifiedDisplay));
    }

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

    public string SizeDisplay
    {
        get
        {
            if (!IsDirectory)
                return Entry.Size is { } bytes ? ValueFormat.Size(bytes, _display.SizeUnit) : string.Empty;

            // Folders: show the (possibly partial) size with a trailing "+" while still counting,
            // or an ellipsis if counting hasn't produced a running total yet.
            if (ComputedSize is { } folderBytes)
                return ValueFormat.Size(folderBytes, _display.SizeUnit) + (IsCalculatingSize ? "+" : string.Empty);
            return IsCalculatingSize ? "…" : string.Empty;
        }
    }

    public string TypeDisplay => Entry switch
    {
        { Kind: FileSystemEntryKind.Drive } => "Drive",
        { IsDirectory: true } => "Folder",
        { Extension: "" } => "File",
        var e => e.Extension.ToUpperInvariant() + " file",
    };

    public string ModifiedDisplay => ValueFormat.Date(Entry.Modified, _display.DateDisplay);
}
