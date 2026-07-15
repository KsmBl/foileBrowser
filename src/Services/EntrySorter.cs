using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>
/// Pure, UI-independent ordering of directory entries (PRD §6.1: "Sorting by any column,
/// ascending/descending"). Kept side-effect-free so it can be unit tested directly.
/// </summary>
public static class EntrySorter
{
    /// <summary>
    /// Orders <paramref name="entries"/> by <paramref name="column"/> and <paramref name="direction"/>.
    /// Directories and drives are always grouped ahead of files regardless of direction — the
    /// familiar file-manager convention — with the column applied within each group. Name is used
    /// as a stable tie-breaker so equal keys keep a deterministic order.
    /// </summary>
    public static IReadOnlyList<FileSystemEntry> Sort(
        IEnumerable<FileSystemEntry> entries, SortColumn column, SortDirection direction)
    {
        var descending = direction == SortDirection.Descending;

        var ordered = entries
            .OrderByDescending(e => e.IsDirectory) // folders first, both directions
            .ThenBy(e => new EntryKey(e, column), new EntryKeyComparer(descending))
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

        return ordered.ToList();
    }

    private readonly record struct EntryKey(FileSystemEntry Entry, SortColumn Column);

    private sealed class EntryKeyComparer(bool descending) : IComparer<EntryKey>
    {
        public int Compare(EntryKey x, EntryKey y)
        {
            var result = CompareColumn(x.Entry, y.Entry, x.Column);
            return descending ? -result : result;
        }

        private static int CompareColumn(FileSystemEntry a, FileSystemEntry b, SortColumn column) => column switch
        {
            SortColumn.Name => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            SortColumn.Size => Nullable.Compare(a.Size, b.Size),
            SortColumn.Type => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase),
            SortColumn.Modified => Nullable.Compare(a.Modified, b.Modified),
            _ => 0,
        };
    }
}
