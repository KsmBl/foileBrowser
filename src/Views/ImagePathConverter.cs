using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace FoileBrowser.Views;

/// <summary>
/// Loads an image file path into a down-scaled <see cref="Bitmap"/> for preview (PRD §6.5).
/// Decoding to a bounded width keeps memory sane and acts as a lightweight thumbnail.
/// Returns null on any error so the UI simply shows nothing.
/// </summary>
public sealed class ImagePathConverter : IValueConverter
{
    public static readonly ImagePathConverter Instance = new();

    private const int DecodeWidth = 640;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, DecodeWidth);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
