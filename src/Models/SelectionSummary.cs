namespace FoileBrowser.Models;

/// <summary>
/// Builds the inspector's combined view of a multi-item selection (PRD §6.5): counts, total and
/// average size, the largest item, the modified-date range, and a per-type breakdown. Pure and
/// display-ready, so it's testable without any UI.
/// </summary>
public static class SelectionSummary
{
    /// <summary>How many distinct file types to list before collapsing the rest into "other".</summary>
    private const int MaxTypeRows = 12;

    public static PreviewResult Build(IReadOnlyList<FileSystemEntry> entries, SizeUnit unit, DateDisplay dates)
    {
        var folders = entries.Count(e => e.IsDirectory);
        var files = entries.Count - folders;

        // Folder sizes are unknown until walked, so every size figure here is "files only" and says so.
        var sized = entries.Where(e => !e.IsDirectory && e.Size is not null).ToList();
        var total = sized.Sum(e => e.Size!.Value);

        List<string> lines =
        [
            $"Items          {entries.Count:N0}   ({files:N0} file(s), {folders:N0} folder(s))",
        ];

        if (sized.Count > 0)
        {
            var average = total / sized.Count;
            var largest = sized.MaxBy(e => e.Size!.Value)!;
            var smallest = sized.MinBy(e => e.Size!.Value)!;

            lines.Add($"Total size     {ValueFormat.Size(total, unit)}   ({total:N0} bytes)");
            lines.Add($"Average size   {ValueFormat.Size(average, unit)}");
            lines.Add($"Largest        {ValueFormat.Size(largest.Size!.Value, unit)}   {largest.Name}");
            if (sized.Count > 1)
                lines.Add($"Smallest       {ValueFormat.Size(smallest.Size!.Value, unit)}   {smallest.Name}");
        }

        if (folders > 0)
            lines.Add($"{Environment.NewLine}Folder contents aren't counted — sizes above cover the {sized.Count:N0} selected file(s) only.");

        AppendDateRange(lines, entries, dates);
        AppendTypeBreakdown(lines, entries, unit);

        return new PreviewResult
        {
            Kind = PreviewKind.Text,
            Title = $"{entries.Count:N0} items selected",
            Info = sized.Count > 0
                ? $"{ValueFormat.Size(total, unit)} in {files:N0} file(s)" + (folders > 0 ? $" · {folders:N0} folder(s)" : "")
                : $"{files:N0} file(s) · {folders:N0} folder(s)",
            Text = string.Join(Environment.NewLine, lines),
        };
    }

    private static void AppendDateRange(List<string> lines, IReadOnlyList<FileSystemEntry> entries, DateDisplay dates)
    {
        var stamps = entries.Where(e => e.Modified is not null).Select(e => e.Modified!.Value).ToList();
        if (stamps.Count == 0)
            return;

        var oldest = stamps.Min();
        var newest = stamps.Max();
        lines.Add(string.Empty);
        lines.Add(oldest == newest
            ? $"Modified       {ValueFormat.Date(oldest, dates)}"
            : $"Modified       {ValueFormat.Date(oldest, dates)}  →  {ValueFormat.Date(newest, dates)}");
    }

    private static void AppendTypeBreakdown(List<string> lines, IReadOnlyList<FileSystemEntry> entries, SizeUnit unit)
    {
        var groups = entries
            .Where(e => !e.IsDirectory)
            .GroupBy(e => e.Extension.Length > 0 ? e.Extension.ToUpperInvariant() : "(no extension)")
            .Select(g => (Type: g.Key, Count: g.Count(), Bytes: g.Sum(e => e.Size ?? 0)))
            .OrderByDescending(g => g.Bytes)
            .ThenBy(g => g.Type, StringComparer.Ordinal)
            .ToList();

        if (groups.Count == 0)
            return;

        lines.Add(string.Empty);
        lines.Add("File types");

        foreach (var (type, count, bytes) in groups.Take(MaxTypeRows))
            lines.Add($"  {type,-16} {count,6:N0}   {ValueFormat.Size(bytes, unit)}");

        if (groups.Count > MaxTypeRows)
        {
            var rest = groups.Skip(MaxTypeRows).ToList();
            lines.Add($"  {$"… {rest.Count:N0} other type(s)",-16} {rest.Sum(g => g.Count),6:N0}   {ValueFormat.Size(rest.Sum(g => g.Bytes), unit)}");
        }
    }
}
