using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Drawing;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

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

    /// <summary>
    /// A square thumbnail of <paramref name="edge"/> pixels, letterboxed so every gallery cell is the
    /// same size whatever the picture's shape, or null when the file will not decode. Runs on a
    /// worker thread — nothing here touches the UI.
    /// </summary>
    public static IImage? Thumbnail(string path, int edge)
    {
        var decoded = DecodePixels(path, edge);
        if (decoded is not { } source)
            return null;

        var square = Letterbox(source, edge);
        return new AnimatedImage(new DecodedImage(edge, edge, [new ImageFrame(square, 0)]));
    }

    /// <summary>
    /// The raw pixels of an image, no larger than <paramref name="wanted"/> on its longest edge. The
    /// toolkit's decoder is tried first (no native library, and it is the only one that reads a PCX
    /// or an ICO here); Skia takes the rest and subsamples on the way in where the format allows.
    /// </summary>
    private static (int Width, int Height, int[] Pixels)? DecodePixels(string path, int wanted)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        var size = ReadHeaderSize(bytes);
        if (size is { } known && (long)known.Width * known.Height <= MaxPixels)
        {
            try
            {
                var image = ImageDecoder.Decode(bytes);
                var frame = image.Frames[0];
                return (image.Width, image.Height, frame.Argb);
            }
            catch (Exception)
            {
                // Not a format it reads after all.
            }
        }

        var failure = Failure.None;
        return ManagedPixels(path, wanted, ref failure);
    }

    /// <summary>Scales into a transparent square by nearest-neighbour sampling — ample for a cell.</summary>
    private static int[] Letterbox((int Width, int Height, int[] Pixels) source, int edge)
    {
        var scale = Math.Min((double)edge / source.Width, (double)edge / source.Height);
        var width = Math.Clamp((int)Math.Round(source.Width * scale), 1, edge);
        var height = Math.Clamp((int)Math.Round(source.Height * scale), 1, edge);
        var left = (edge - width) / 2;
        var top = (edge - height) / 2;

        var square = new int[edge * edge];
        for (var y = 0; y < height; ++y)
        {
            var sourceRow = Math.Min(source.Height - 1, y * source.Height / height) * source.Width;
            var targetRow = (y + top) * edge;
            for (var x = 0; x < width; ++x)
                square[targetRow + x + left] =
                    source.Pixels[sourceRow + Math.Min(source.Width - 1, x * source.Width / width)];
        }

        return square;
    }

    // ---- the managed format library: everything the toolkit's own decoder does not read ----

    /// <summary>How long each picture is held when a file turns out to hold several.</summary>
    private const int MultiImageDelayMs = 1200;

    private static IImage? LoadScaled(string path, ref Failure failure)
    {
        // A file often holds more than one picture — the icons in an executable, the pages of a
        // TIFF, the sizes inside an .ico — and showing only the first threw the rest away. Where the
        // format can say how many it has, all of them are handed to the picture box, which already
        // knows how to cycle frames because that is how an animated GIF is shown.
        if (ManagedGallery(path, MaxEdge) is { Count: > 1 } gallery)
        {
            var width = gallery.Max(picture => picture.Width);
            var height = gallery.Max(picture => picture.Height);
            var frames = gallery
                .Select(picture => new ImageFrame(Centre(picture, width, height), MultiImageDelayMs))
                .ToArray();

            failure = Failure.None;
            return new AnimatedImage(new DecodedImage(width, height, frames));
        }

        var pixels = ManagedPixels(path, MaxEdge, ref failure);
        return pixels is { } decoded
            ? new AnimatedImage(new DecodedImage(decoded.Width, decoded.Height, [new ImageFrame(decoded.Pixels, 0)]))
            : null;
    }

    /// <summary>
    /// Every picture in a file that holds several, scaled to fit, or null when it holds one.
    /// </summary>
    /// <remarks>
    /// Multi-image support is an optional augmentation on a format entry, so most formats answer
    /// nothing here and fall straight through to the single decode. The count is asked first and the
    /// pictures only pulled when there is more than one, so a plain photograph pays nothing for this.
    /// </remarks>
    private static List<(int Width, int Height, int[] Pixels)>? ManagedGallery(string path, int maxEdge)
    {
        try
        {
            var file = new FileInfo(path);
            var format = FormatRegistry.DetectFromBytes(File.ReadAllBytes(path));
            if (format == ImageFormat.Unknown)
                format = FormatRegistry.DetectFromExtension(file.Extension);

            if (FormatRegistry.GetEntry(format) is not { SupportsMultiImage: true } entry
                || entry.GetImageCount!(file) <= 1
                || entry.LoadAllRawImages?.Invoke(file) is not { Count: > 1 } raws)
                return null;

            var pictures = new List<(int Width, int Height, int[] Pixels)>(raws.Count);
            foreach (var raw in raws)
            {
                if ((long)raw.Width * raw.Height > MaxPixels)
                    continue;

                var decoded = ToPixels(raw);
                var shrink = Fit(decoded.Width, decoded.Height, maxEdge);
                pictures.Add(shrink >= 1f ? decoded : Shrink(decoded, shrink));
            }

            return pictures.Count > 1 ? pictures : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Places a picture in the middle of a transparent canvas, so frames of different
    /// sizes — the 16, 32 and 256-pixel icons of one executable — share one box without stretching.</summary>
    private static int[] Centre((int Width, int Height, int[] Pixels) source, int width, int height)
    {
        if (source.Width == width && source.Height == height)
            return source.Pixels;

        var canvas = new int[width * height];
        var left = (width - source.Width) / 2;
        var top = (height - source.Height) / 2;
        for (var y = 0; y < source.Height; ++y)
            Array.Copy(source.Pixels, y * source.Width, canvas, ((y + top) * width) + left, source.Width);

        return canvas;
    }

    /// <summary>
    /// Decodes through <c>Hawkynt.FileFormats.Images</c>, no larger than <paramref name="maxEdge"/>
    /// on its longest side.
    /// </summary>
    /// <remarks>
    /// Pure managed code across ~580 formats, which is what let the SkiaSharp dependency and its
    /// 9 MB native library go (PRD §6.12). One thing is given up with it: Skia could subsample a
    /// JPEG while decoding, so an enormous photo never existed at full size in memory. Here the
    /// decode is full-size and the scaling happens after, which is why the pixel-count guard below
    /// runs on the *header* before any pixels are allocated — the bound that used to be a nicety is
    /// now what stops a decompression bomb.
    /// </remarks>
    private static (int Width, int Height, int[] Pixels)? ManagedPixels(string path, int maxEdge, ref Failure failure)
    {
        try
        {
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

            // Both opinions, tried in that order. The magic-byte table is right far more often than a
            // file name, but it is not infallible and it does not answer "unknown" when it is wrong:
            // a Targa has no signature at its head, so the table reads it as a Gaf, and an XPM is C
            // source that comes back as a Sun icon. Reading with only the first answer meant those
            // decoded as nothing at all, even though the reader their extension names is right there.
            var byContent = FormatRegistry.DetectFromBytes(bytes);
            var byName = FormatRegistry.DetectFromExtension(Path.GetExtension(path));
            if (byContent == ImageFormat.Unknown && byName == ImageFormat.Unknown)
            {
                failure = Failure.Unreadable;
                return null;
            }

            // Read the dimensions from the header where the format can say, and refuse before
            // decoding. A 50000×50000 PNG is a few hundred KB on disk and 10 GB in memory.
            var oversized = false;
            var raw = Decode(byContent) ?? (byName != byContent ? Decode(byName) : null);
            if (oversized)
            {
                failure = Failure.TooLarge;
                return null;
            }

            if (raw is null)
            {
                failure = Failure.Unreadable;
                return null;
            }

            RawImage? Decode(ImageFormat format)
            {
                if (format == ImageFormat.Unknown || FormatRegistry.GetEntry(format) is not { } entry)
                    return null;

                if (entry.ReadImageInfo?.Invoke(bytes) is { } info && (long)info.Width * info.Height > MaxPixels)
                {
                    oversized = true;
                    return null;
                }

                try
                {
                    return entry.LoadRawImageFromBytes(bytes);
                }
                catch (Exception)
                {
                    // Wrong reader for this file after all; the caller tries the other opinion.
                    return null;
                }
            }

            if ((long)raw.Width * raw.Height > MaxPixels)
            {
                failure = Failure.TooLarge;
                return null;
            }

            var decoded = ToPixels(raw);
            var shrink = Fit(decoded.Width, decoded.Height, maxEdge);
            return shrink >= 1f ? decoded : Shrink(decoded, shrink);
        }
        catch (Exception)
        {
            // A malformed file of a format the library does claim: not a crash, just not a preview.
            failure = Failure.Unreadable;
            return null;
        }
    }

    private static float Fit(int width, int height, int maxEdge)
    {
        var longest = Math.Max(width, height);
        return longest <= maxEdge ? 1f : (float)maxEdge / longest;
    }

    /// <summary>Nearest-neighbour reduction — the same sampling <see cref="Letterbox"/> uses.</summary>
    private static (int Width, int Height, int[] Pixels) Shrink(
        (int Width, int Height, int[] Pixels) source, float scale)
    {
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));
        var pixels = new int[width * height];
        for (var y = 0; y < height; ++y)
        {
            var sourceRow = Math.Min(source.Height - 1, y * source.Height / height) * source.Width;
            var targetRow = y * width;
            for (var x = 0; x < width; ++x)
                pixels[targetRow + x] = source.Pixels[sourceRow + Math.Min(source.Width - 1, x * source.Width / width)];
        }

        return (width, height, pixels);
    }

    /// <summary>
    /// Wraps a decoded image as toolkit pixels. The toolkit packs a pixel as
    /// <c>(a &lt;&lt; 24) | (r &lt;&lt; 16) | (g &lt;&lt; 8) | b</c>, which is byte-for-byte what
    /// BGRA32 already holds on a little-endian machine, so the bytes are reinterpreted rather than
    /// converted once the image is in that layout.
    /// </summary>
    private static (int Width, int Height, int[] Pixels) ToPixels(RawImage image)
    {
        var bgra = image.Format == PixelFormat.Bgra32 ? image : PixelConverter.Convert(image, PixelFormat.Bgra32);
        return (bgra.Width, bgra.Height, MemoryMarshal.Cast<byte, int>(bgra.PixelData).ToArray());
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
