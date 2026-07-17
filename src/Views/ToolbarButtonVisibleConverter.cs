using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace FoileBrowser.Views;

/// <summary>
/// Binds a toolbar button's visibility to the "hidden buttons" set: the button id passed as the
/// converter parameter is visible unless it appears in the bound collection (PRD §6.8).
/// </summary>
public sealed class ToolbarButtonVisibleConverter : IValueConverter
{
    public static readonly ToolbarButtonVisibleConverter Instance = new();

    public object Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => value is not IEnumerable<string> hidden || parameter is not string id || !hidden.Contains(id);

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => throw new System.NotSupportedException();
}
