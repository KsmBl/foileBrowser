using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Drawing;
using SkiaSharp;

namespace FoileBrowser.Views;

/// <summary>
/// Decodes a file into something the inspector's picture box can show (PRD §6.5).
///
/// The toolkit's own decoder is tried first: it needs no native image library and it keeps an
/// animated GIF animated. Anything it does not read — JPEG, WebP, TIFF, … — and anything too big to
/// decode at full size goes through SkiaSharp, which is already in the graph for the metadata
/// columns, and is scaled down on the way in. Either way the result is the 32-bit ARGB the toolkit
/// draws from, so there is no intermediate bitmap file.
/// </summary>
internal static class PreviewImage
{
    /// <summary>Longest edge a preview is decoded to; a panel never shows more than this.</summary>
    private const int MaxEdge = 2048;

    /// <summary>The most pixels worth materialising at once (~160 MB as ARGB).</summary>
    private const long MaxPixels = 40_000_000;

    /// <summary>Why a file has no preview, so the panel can say which it was.</summary>
    internal enum Failure
    {
        None,
        Unreadable,
        TooLarge,
    }

    public static IImage? Load(string path, out Failure failure)
    {
        failure = Failure.None;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            failure = Failure.Unreadable;
            return null;
        }

        // The toolkit's decoder sizes its pixel buffer straight from the header, so the dimensions
        // are checked here first — a small file can still declare an enormous image.
        var size = ReadHeaderSize(bytes);
        if (size is { } known && (long)known.Width * known.Height <= MaxPixels
            && known.Width <= MaxEdge && known.Height <= MaxEdge)
        {
            try
            {
                return new AnimatedImage(ImageDecoder.Decode(bytes));
            }
            catch (Exception)
            {
                // Not a format it reads after all — fall through to the scaling decoder.
            }
        }

        return LoadScaled(path, ref failure);
    }

    // ---- SkiaSharp: everything else, decoded no larger than we will draw it ----

    private static IImage? LoadScaled(string path, ref Failure failure)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null)
            {
                failure = Failure.Unreadable;
                return null;
            }

            var source = codec.Info;
            var scale = Fit(source.Width, source.Height);

            // Only some formats (JPEG) can subsample while decoding; the rest report full size and
            // are scaled after the fact, which is why the decoded size is still capped here.
            var dimensions = scale < 1f ? codec.GetScaledDimensions(scale) : new SKSizeI(source.Width, source.Height);
            if ((long)dimensions.Width * dimensions.Height > MaxPixels)
            {
                failure = Failure.TooLarge;
                return null;
            }

            var info = new SKImageInfo(dimensions.Width, dimensions.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var decoded = new SKBitmap(info);
            var result = codec.GetPixels(info, decoded.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                failure = Failure.Unreadable;
                return null;
            }

            var shrink = Fit(decoded.Width, decoded.Height);
            if (shrink >= 1f)
                return ToImage(decoded);

            using var reduced = new SKBitmap(new SKImageInfo(
                Math.Max(1, (int)(decoded.Width * shrink)),
                Math.Max(1, (int)(decoded.Height * shrink)),
                SKColorType.Bgra8888,
                SKAlphaType.Unpremul));
            return decoded.ScalePixels(reduced, SKFilterQuality.Medium) ? ToImage(reduced) : ToImage(decoded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            failure = Failure.Unreadable;
            return null;
        }
    }

    private static float Fit(int width, int height)
    {
        var longest = Math.Max(width, height);
        return longest <= MaxEdge ? 1f : (float)MaxEdge / longest;
    }

    /// <summary>
    /// Wraps a BGRA8888 bitmap as a toolkit image. The toolkit packs a pixel as
    /// <c>(a &lt;&lt; 24) | (r &lt;&lt; 16) | (g &lt;&lt; 8) | b</c>, which is byte-for-byte what
    /// Skia's BGRA8888 already holds on a little-endian machine, so the pixels are reinterpreted
    /// rather than converted.
    /// </summary>
    private static IImage ToImage(SKBitmap bitmap)
    {
        var pixels = MemoryMarshal.Cast<byte, int>(bitmap.GetPixelSpan()).ToArray();
        var frame = new ImageFrame(pixels, 0);
        return new AnimatedImage(new DecodedImage(bitmap.Width, bitmap.Height, [frame]));
    }

    // ---- header sniffing, for the formats the toolkit's own decoder accepts ----

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Walks a JPEG's marker segments to its frame header and reads the size from it. Standalone
    /// markers carry no length, a segment's length covers its own two bytes, and any SOF but the
    /// arithmetic and lossless ones carries height then width at a fixed offset.
    /// </summary>
    private static (int Width, int Height)? JpegSize(ReadOnlySpan<byte> bytes)
    {
        var offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
                return null;

            var marker = bytes[offset + 1];
            offset += 2;

            // Padding fill bytes and the standalone markers (RSTn, SOI, EOI, TEM) carry no segment.
            if (marker == 0xFF)
            {
                --offset;
                continue;
            }

            if (marker is 0x01 or 0xD8 or 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
                continue;

            if (offset + 2 > bytes.Length)
                return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            if (length < 2 || offset + length > bytes.Length)
                return null;

            // SOF0..SOF15, excluding the DHT/JPG/DAC markers interleaved in that range.
            if (marker >= 0xC0 && marker <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (length < 7)
                    return null;
                return (
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..]));
            }

            offset += length;
        }

        return null;
    }

    /// <summary>
    /// The declared pixel size of a PNG, GIF, BMP, ICO/CUR or PCX, or null when the bytes are not
    /// one of those. Only the header is read — nothing is decoded.
    /// </summary>
    private static (int Width, int Height)? ReadHeaderSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(PngSignature))
            return (
                (int)BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]),
                (int)BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]));

        // SOI followed by a marker. Dimensions live in the frame header, which is found by walking
        // the marker segments — cheap, and it keeps a photo off the SkiaSharp path entirely.
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
            && JpegSize(bytes) is { } jpeg)
            return jpeg;

        if (bytes.Length >= 10 && bytes[..4].SequenceEqual("GIF8"u8))
            return (
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]));

        // BMP with a BITMAPINFOHEADER or later; the 12-byte core header is left to Skia.
        if (bytes.Length >= 26 && bytes[0] == 'B' && bytes[1] == 'M'
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes[14..]) >= 40)
            return (
                Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes[18..])),
                Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes[22..])));

        // ICO/CUR: the directory's first entry carries the size, where 0 means 256.
        if (bytes.Length >= 8 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] is 1 or 2 && bytes[3] == 0)
            return (bytes[6] == 0 ? 256 : bytes[6], bytes[7] == 0 ? 256 : bytes[7]);

        if (bytes.Length >= 12 && bytes[0] == 0x0A)
        {
            var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]) - BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) + 1;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]) - BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) + 1;
            if (width > 0 && height > 0)
                return (width, height);
        }

        return null;
    }
}
