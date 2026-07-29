using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FoileBrowser.Tests;

/// <summary>
/// Builds real encoded images for tests that need a file of a given format and size.
/// </summary>
/// <remarks>
/// Encoded through the same library the app decodes with, which is the point: a fixture written by
/// the thing under test would prove nothing, but this is the library's *writer* feeding its reader,
/// and both sides are exercised. The format is looked up by extension rather than named, because
/// the ImageFormat enum is source-generated from whichever format assemblies are present.
/// </remarks>
internal static class ImageFixture
{
    /// <summary>A solid cornflower-blue image of the given size, encoded as <paramref name="extension"/>.</summary>
    internal static byte[] Encode(string extension, int width, int height)
    {
        var format = FormatRegistry.DetectFromExtension(extension);
        Assert.That(format, Is.Not.EqualTo(ImageFormat.Unknown), $"no writer registered for \"{extension}\"");

        // BGRA32, which is the layout the app converts everything to anyway.
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = 0xED; // B
            pixels[i + 1] = 0x95; // G
            pixels[i + 2] = 0x64; // R
            pixels[i + 3] = 0xFF; // A
        }

        var raw = new RawImage
        {
            Width = width,
            Height = height,
            Format = PixelFormat.Bgra32,
            PixelData = pixels,
        };

        var bytes = FormatRegistry.Write(raw, format);
        Assert.That(bytes, Is.Not.Null, $"\"{extension}\" could not be encoded");
        return bytes!;
    }
}
