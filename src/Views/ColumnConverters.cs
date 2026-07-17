using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using FoileBrowser.Models;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

/// <summary>Resolves a data row's cell text from [columnId, entry, cellVersion] (PRD §6.1). The version
/// is only there to re-trigger the binding when a lazily-computed value (size/metadata) arrives.</summary>
public sealed class CellTextConverter : IMultiValueConverter
{
    public static readonly CellTextConverter Instance = new();

    public object Convert(IList<object?> values, System.Type targetType, object? parameter, CultureInfo culture)
        => values.Count >= 2 && values[0] is string id && values[1] is FileEntryViewModel entry
            ? entry.GetCellText(id)
            : string.Empty;
}

/// <summary>Sort arrow for a column header from [activeSortColumn, activeSortDirection, thisColumnSort].</summary>
public sealed class SortGlyphConverter : IMultiValueConverter
{
    public static readonly SortGlyphConverter Instance = new();

    public object Convert(IList<object?> values, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 3 && values[2] is SortColumn columnSort
            && values[0] is SortColumn active && active == columnSort)
            return values[1] is SortDirection.Ascending ? "▲" : "▼";
        return string.Empty;
    }
}

/// <summary>Right-aligns numeric columns (Size, …), left-aligns the rest.</summary>
public static class ColumnConverters
{
    public static readonly IValueConverter Alignment =
        new FuncValueConverter<bool, HorizontalAlignment>(right => right ? HorizontalAlignment.Right : HorizontalAlignment.Left);
}
