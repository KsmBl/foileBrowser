using System.Globalization;
using Avalonia.Data.Converters;

namespace FoileBrowser.ViewModels;

/// <summary>Dims hidden entries: true -> 0.5 opacity, false -> 1.0 (PRD §6.1 hidden files).</summary>
public sealed class BoolOpacityConverter : IValueConverter
{
    public static readonly BoolOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 0.5 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
