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

    /// <summary>Bumped whenever any cell value changes (size computed, metadata arrived, display toggled),
    /// so the data-driven column cells refresh through their converter binding (PRD §6.1).</summary>
    [ObservableProperty]
    private int _cellVersion;

    partial void OnComputedSizeChanged(long? value) => CellVersion++;
    partial void OnIsCalculatingSizeChanged(bool value) => CellVersion++;

    /// <summary>Re-renders size/date after a display-mode toggle without rebuilding the list (PRD §6.1/§6.2).</summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(SizeDisplay));
        OnPropertyChanged(nameof(ModifiedDisplay));
        CellVersion++;
    }

    /// <summary>The display text for a column by id — the value shown in that column's cell (PRD §6.1).
    /// Metadata columns are resolved lazily by <see cref="Metadata"/> (null until wired), else blank.</summary>
    public string GetCellText(string columnId) => columnId switch
    {
        "name" => Name,
        "size" => SizeDisplay,
        "type" => TypeDisplay,
        "modified" => ModifiedDisplay,
        "extension" => Entry.Extension,
        "location" => LocationDisplay ?? string.Empty,
        _ => Metadata?.Invoke(this, columnId) ?? string.Empty,
    };

    /// <summary>
    /// The number a heat map ranks this cell by, or null when there is nothing to rank — an
    /// unmeasured folder, a column that does not apply to this file, a value still computing
    /// (PRD §6.1).
    /// </summary>
    /// <remarks>
    /// The built-in columns answer from the entry itself, exactly. A metadata column has only ever
    /// produced display text ("1920×1080", "5.2 Mbps"), so its rank is the first number in that text
    /// — which is the dimension, the rate, the duration, the count. Reading the leading number is a
    /// guess, but it is the same guess for every row in the column, so the ordering it produces is
    /// the ordering the column shows.
    /// </remarks>
    public double? GetHeatValue(string columnId) => columnId switch
    {
        "size" => IsDirectory ? ComputedSize : Entry.Size,
        "modified" => Entry.Modified?.Ticks,
        "name" or "type" or "extension" or "location" => null,
        _ => LeadingNumber(GetCellText(columnId)),
    };

    /// <summary>The first number in a piece of display text, or null when it opens with none.</summary>
    private static double? LeadingNumber(string text)
    {
        var start = 0;
        while (start < text.Length && !char.IsAsciiDigit(text[start]))
            ++start;

        if (start == text.Length)
            return null;

        var end = start;
        var seenPoint = false;
        while (end < text.Length)
        {
            if (char.IsAsciiDigit(text[end]))
                ++end;
            else if (text[end] is '.' && !seenPoint && end + 1 < text.Length && char.IsAsciiDigit(text[end + 1]))
            {
                seenPoint = true;
                ++end;
            }
            else
                break;
        }

        return double.TryParse(
            text.AsSpan(start, end - start),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    /// <summary>Set by the owner to resolve metadata columns (image/audio/video) lazily.</summary>
    public Func<FileEntryViewModel, string, string>? Metadata { get; set; }

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
