using CommunityToolkit.Mvvm.ComponentModel;

namespace FoileBrowser.ViewModels;

/// <summary>Which metadata source computes a column's value (drives lazy background computation).</summary>
public enum ColumnKind
{
    /// <summary>Read straight off the directory entry (name/size/type/modified/…).</summary>
    Builtin,
    Image,
    Audio,
    Video,
}

/// <summary>
/// A file-list column: its id (also the metadata key), header text, live width and alignment
/// (PRD §6.1). One shared, ordered collection of these drives both the header row and every data row,
/// so they always line up; resizing/reordering/toggling a column updates that one collection.
/// </summary>
public sealed partial class ColumnSpec : ObservableObject
{
    public required string Id { get; init; }
    public required string Header { get; init; }
    public ColumnKind Kind { get; init; } = ColumnKind.Builtin;
    public bool RightAligned { get; init; }
    public double DefaultWidth { get; init; } = 120;

    /// <summary>Live column width in px (bound by header + row cells); persisted.</summary>
    [ObservableProperty]
    private double _width;

    /// <summary>The <see cref="Models.SortColumn"/> this maps to, or null when the column isn't sortable.</summary>
    public Models.SortColumn? Sort { get; init; }
}

/// <summary>The catalogue of columns the user can show (built-ins now; metadata columns are appended
/// once their providers exist). Ids are stable — they key persistence and metadata lookups.</summary>
public static class ColumnCatalog
{
    public static IReadOnlyList<ColumnSpec> All { get; private set; } = BuiltIn();

    /// <summary>Default visible columns (in order) for a fresh profile.</summary>
    public static readonly string[] DefaultVisible = ["name", "size", "type", "modified"];

    private static List<ColumnSpec> BuiltIn() =>
    [
        new() { Id = "name", Header = "Name", DefaultWidth = 260, Sort = Models.SortColumn.Name },
        new() { Id = "size", Header = "Size", DefaultWidth = 110, RightAligned = true, Sort = Models.SortColumn.Size },
        new() { Id = "type", Header = "Type", DefaultWidth = 110, Sort = Models.SortColumn.Type },
        new() { Id = "modified", Header = "Modified", DefaultWidth = 150, Sort = Models.SortColumn.Modified },
        new() { Id = "extension", Header = "Ext", DefaultWidth = 70 },
        new() { Id = "location", Header = "Location", DefaultWidth = 220 },
    ];

    /// <summary>Registers additional columns (e.g. metadata) into the catalogue.</summary>
    public static void Register(IEnumerable<ColumnSpec> columns) =>
        All = All.Concat(columns).ToList();

    /// <summary>A fresh <see cref="ColumnSpec"/> instance for <paramref name="id"/> (so widths are per-profile).</summary>
    public static ColumnSpec? Create(string id)
    {
        var template = All.FirstOrDefault(c => c.Id == id);
        return template is null ? null : new ColumnSpec
        {
            Id = template.Id,
            Header = template.Header,
            Kind = template.Kind,
            RightAligned = template.RightAligned,
            DefaultWidth = template.DefaultWidth,
            Sort = template.Sort,
            Width = template.DefaultWidth,
        };
    }
}
