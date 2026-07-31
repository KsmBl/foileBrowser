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
            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[HeaderBytes];
            var read = stream.Read(head);
            if (read <= 0)
                return false;

            var format = FormatRegistry.DetectFromBytes(head[..read]);
            return format != ImageFormat.Unknown && FormatRegistry.GetEntry(format) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

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

    /// <summary>Extensions that name both something decodable and something enterable.</summary>
    private static readonly Lazy<HashSet<string>> Contested = new(() =>
    {
        ArchiveService.EnsureFormatsRegistered();
        var pictures = DecodableExtensions().ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Compression.Registry.FormatRegistry.All
            .Where(descriptor => descriptor.Capabilities.HasFlag(Compression.Registry.FormatCapabilities.CanList))
            .SelectMany(descriptor => descriptor.Extensions)
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .Where(pictures.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
