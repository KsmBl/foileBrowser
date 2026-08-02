using Hawkynt.FileFormats.Images;

namespace FoileBrowser.Services;

/// <summary>
/// Which files the shipped image library can decode — asked of its registry rather than listed here.
/// </summary>
/// <remarks>
/// <para>
/// The preview, the gallery thumbnails and the metadata columns each used to carry a hand-written
/// list of seven to fourteen extensions, while the library underneath them reads ~580 formats and
/// the decode path has always gone straight to <see cref="FormatRegistry"/>. So the lists were not a
/// policy about what to show, they were just the year they were written: an Amiga IFF, a Sun raster
/// or a Dr. Halo CUT decoded perfectly well and was never offered to the decoder.
/// </para>
/// <para>
/// A registered extension is not the same as a readable one. Some formats are registered for
/// detection only — enough to name the file, with no reader behind it — and those have no
/// <see cref="FormatEntry"/>, which is exactly what separates the two here (PRD §6.5).
/// </para>
/// </remarks>
internal static class ImageSupport
{
    /// <summary>Whether this file's extension names a format the library can decode.</summary>
    public static bool CanDecode(string path) => CanDecodeExtension(Path.GetExtension(path));

    /// <summary>
    /// Whether a file met while sweeping a folder should go into the gallery, on the strength of its
    /// name alone.
    /// </summary>
    /// <remarks>
    /// A sweep meets thousands of files nobody pointed at, so it gets the strict rule: the name has
    /// to be unambiguous. The forgiving one — open it and see — belongs to the single file a person
    /// actually selected, where the work is worth it and a mislabelled picture is worth finding. Here
    /// it would mean reading every <c>.cs</c> file in a source tree to discover that none of them is
    /// an Atari screen.
    /// </remarks>
    public static bool NameAloneSaysPicture(string path)
        => ExtensionAloneIsEnough(Path.GetExtension(path));

    /// <summary>How much of a file the magic-byte table ever needs; its own default peek is 64.</summary>
    private const int HeaderBytes = 512;

    /// <summary>
    /// Whether the file's own first bytes name a format the library can decode.
    /// </summary>
    /// <remarks>
    /// Worth the read where the answer decides which panel a file opens in. 25 extensions are claimed
    /// by both this library and the archive registry — <c>.exe</c>, <c>.dll</c>, <c>.obj</c>,
    /// <c>.dat</c>, <c>.img</c> among them — and an extension is a weak claim on any of them: a
    /// Wavefront <c>.obj</c> is plain text that a raster format of the same name would never read.
    /// Asking the content first is also what the decoder itself does, so the gate and the decode
    /// cannot disagree.
    /// </remarks>
    public static bool ContentIsDecodable(string path)
    {
        try
        {
            long length;
            using (var stream = File.OpenRead(path))
            {
                length = stream.Length;
                Span<byte> head = stackalloc byte[HeaderBytes];
                var read = stream.Read(head);
                if (read <= 0)
                    return false;

                if (Readable(FormatRegistry.DetectFromBytes(head[..read])))
                    return true;
            }

            // No signature matched, which settles nothing on its own: plenty of the older rasters
            // carry no magic bytes at all. The name is the only other witness, and for a contested
            // extension it is not a good one — so the doubt is resolved by simply trying to decode,
            // and a file that yields a picture is shown as one whatever it is called. Bounded by
            // size because this runs for one selected file rather than for a listing, and only ever
            // reached for a name the image registry already claims.
            var named = FormatRegistry.DetectFromExtension(Path.GetExtension(path));
            if (!Readable(named) || length > MaxProbeBytes)
                return false;

            return FormatRegistry.GetEntry(named)!.LoadRawImageFromBytes(File.ReadAllBytes(path)) is not null;
        }
        catch (Exception)
        {
            // Unreadable, or the wrong reader after all — either way, not a picture.
            return false;
        }
    }

    /// <summary>The largest file a decode will be attempted on merely to find out what it is.</summary>
    private const long MaxProbeBytes = 32L * 1024 * 1024;

    private static bool Readable(ImageFormat format)
        => format != ImageFormat.Unknown && FormatRegistry.GetEntry(format) is not null;

    /// <summary>As above, for an extension that may or may not carry its leading dot.</summary>
    public static bool CanDecodeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        var format = FormatRegistry.DetectFromExtension(extension);
        return format != ImageFormat.Unknown && FormatRegistry.GetEntry(format) is not null;
    }

    /// <summary>
    /// Whether the extension alone is enough to open a file in the picture panel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name is good evidence right up until two of the shipped libraries want it. 25 extensions are
    /// claimed by both this catalogue and the archive registry — <c>.exe</c>, <c>.dll</c>,
    /// <c>.obj</c>, <c>.dat</c>, <c>.img</c>, <c>.wad</c> and the rest — and for those the extension
    /// says nothing at all, so the file has to earn the picture panel with its own bytes. The set is
    /// read off the two registries rather than written down here, so it tracks whatever the packages
    /// ship next.
    /// </para>
    /// <para>
    /// Everything else keeps the older, more forgiving behaviour, and deliberately: a truncated or
    /// corrupt <c>.png</c> is still a PNG, and answering "here is a picture that will not open"
    /// beats rendering three control characters as if they were text.
    /// </para>
    /// </remarks>
    public static bool ExtensionAloneIsEnough(string extension)
        => CanDecodeExtension(extension)
            && !Contested.Value.Contains(extension.TrimStart('.').ToLowerInvariant());

    /// <summary>
    /// Names that mean text far more often than they mean anything else, whatever else claims them.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="PreviewService"/>, which decides from the same list whether a file is
    /// worth showing as text; a name cannot be both the obvious text case there and unambiguous
    /// evidence of a picture here.
    /// </remarks>
    internal static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "txt", "md", "log", "json", "xml", "yaml", "yml", "csv", "ini", "cfg", "conf",
            "cs", "js", "ts", "py", "java", "c", "cpp", "h", "hpp", "go", "rs", "rb", "php",
            "html", "css", "sh", "bat", "ps1", "sql", "toml", "gitignore", "editorconfig",
        };

    /// <summary>Extensions that name a picture and something far more common besides.</summary>
    /// <remarks>
    /// Two registries collide here and both matter. The archives claim 25 of the picture names —
    /// <c>.exe</c>, <c>.dll</c>, <c>.obj</c>, <c>.dat</c>, <c>.img</c> among them — and source code
    /// claims four more, which are the ones that hurt: <c>.cs</c> is an Atari StarPainter screen,
    /// <c>.cpp</c> an Amstrad CPC Plus one, <c>.rs</c> a Sun raster and <c>.csv</c> a table of pixel
    /// values. Trusting the name meant a folder of source code read as a folder of pictures — a
    /// checkout of any C# repository filled the gallery with several hundred <c>.cs</c> files and
    /// pushed every real photograph in the tree past the limit.
    /// </remarks>
    private static readonly Lazy<HashSet<string>> Contested = new(() =>
    {
        ArchiveService.EnsureFormatsRegistered();
        var pictures = DecodableExtensions().ToHashSet(StringComparer.OrdinalIgnoreCase);

        var contested = Compression.Registry.FormatRegistry.All
            .Where(descriptor => descriptor.Capabilities.HasFlag(Compression.Registry.FormatCapabilities.CanList))
            .SelectMany(descriptor => descriptor.Extensions)
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .Where(pictures.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        contested.UnionWith(TextExtensions.Where(pictures.Contains));
        return contested;
    });

    /// <summary>Every extension a decodable format claims, lowercased and without its dot.</summary>
    /// <remarks>Used by the tests to hold this against the registry rather than against a list.</remarks>
    public static IEnumerable<string> DecodableExtensions()
        => FormatRegistry.SupportedReadFormats
            .SelectMany(entry => entry.AllExtensions)
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .Where(extension => extension.Length > 0)
            .Distinct();
}
