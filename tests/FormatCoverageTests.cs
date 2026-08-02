using Compression.Registry;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

/// <summary>
/// Holds the app's coverage against the libraries it ships rather than against a list written here
/// (PRD §6.5, §6.11).
/// </summary>
/// <remarks>
/// These are the tests that would have caught the gap they were written for. The image library reads
/// ~580 formats and the archive registry lists several hundred more, but the app reached them through
/// hand-written extension sets that had been copied forward from the first version of each feature —
/// so a format could be shipped, decodable, and still never offered to the decoder. Asserting against
/// the registry means a format added upstream is covered here the day the package is bumped, and one
/// that stops being readable fails a test rather than quietly becoming a blank cell.
/// </remarks>
[TestFixture]
public class FormatCoverageTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-coverage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        TempTree.Remove(_root);
    }

    // ---- images (PRD §6.5) ----

    [Test]
    public void The_Image_Library_Really_Does_Ship_Its_Whole_Catalogue()
    {
        var extensions = ImageSupport.DecodableExtensions().ToList();

        TestContext.Out.WriteLine($"decodable image extensions: {extensions.Count}");
        Assert.That(
            extensions, Has.Count.GreaterThan(300),
            "the package advertises ~580 formats; a number this far below it means the registry did not initialize");
    }

    [Test]
    public void Every_Decodable_Image_Format_Is_Offered_A_Preview()
    {
        var refused = ImageSupport.DecodableExtensions()
            .Where(extension => !ImageSupport.CanDecodeExtension(extension))
            .ToList();

        Assert.That(refused, Is.Empty, "these formats decode but the preview would not ask for them");
    }

    /// <summary>
    /// The formats the hand-written lists used to stop at, plus a spread of the ones they never
    /// reached — an Amiga IFF, a Sun raster, a Dr. Halo cut, a Kodak photo CD.
    /// </summary>
    [TestCase("png")]
    [TestCase("jpg")]
    [TestCase("gif")]
    [TestCase("bmp")]
    [TestCase("webp")]
    [TestCase("tiff")]
    [TestCase("tga")]
    [TestCase("pcx")]
    [TestCase("iff")]
    [TestCase("ras")]
    [TestCase("cut")]
    [TestCase("pcd")]
    [TestCase("xpm")]
    [TestCase("sgi")]
    public void A_Picture_Gets_A_Picture_Preview(string extension)
    {
        var name = "sample." + extension;
        File.WriteAllBytes(Path.Combine(_root, name), new byte[64]);

        var preview = new PreviewService().CreateAsync(FileEntry(name)).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(preview.Kind, Is.EqualTo(PreviewKind.Image), $".{extension} should preview as a picture");
            Assert.That(ThumbnailService.CanRender(Path.Combine(_root, name)), Is.True,
                $".{extension} should be worth a gallery thumbnail");
        });
    }

    /// <summary>
    /// A real picture is recognised by its bytes, whatever the file is called.
    /// </summary>
    [Test]
    public void A_Picture_Under_The_Wrong_Name_Is_Still_A_Picture()
    {
        // A 1×1 PNG, named as something no image library claims.
        File.WriteAllBytes(Path.Combine(_root, "photo.wexford"), OnePixelPng);

        var preview = new PreviewService().CreateAsync(FileEntry("photo.wexford")).GetAwaiter().GetResult();

        Assert.That(preview.Kind, Is.EqualTo(PreviewKind.Image));
    }

    /// <summary>
    /// The regression the content-first ordering exists to prevent: extensions both libraries claim
    /// are held by text far more often than by the raster format of the same name.
    /// </summary>
    [TestCase("obj", "v 0.0 0.0 0.0\nf 1 2 3\n")]
    [TestCase("dat", "key=value\n")]
    public void A_Text_File_Under_A_Contested_Extension_Still_Reads_As_Text(string extension, string content)
    {
        var name = "notes." + extension;
        File.WriteAllText(Path.Combine(_root, name), content);

        var preview = new PreviewService().CreateAsync(FileEntry(name)).GetAwaiter().GetResult();

        Assert.That(preview.Kind, Is.EqualTo(PreviewKind.Text), $".{extension} holding text should read as text");
    }

    /// <summary>The smallest valid PNG: 8-byte signature, IHDR, a one-pixel IDAT, IEND.</summary>
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE,
        0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54,
        0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00, 0x03, 0x01, 0x01, 0x00,
        0x18, 0xDD, 0x8D, 0xB0,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    /// <summary>
    /// A Targa decodes even though the magic-byte table names another format for it.
    /// </summary>
    /// <remarks>
    /// Targa keeps its signature in a footer, not at its head, so the byte table has nothing to match
    /// and lands on whichever headerless format sorts first — a Gaf, as it happens. It does not answer
    /// "unknown", so reading with only that opinion decoded a perfectly good TGA as nothing. The
    /// decoder tries the extension's reader as well, which is why this passes. Notably .tga was in the
    /// old hand-written thumbnail list all along: it was offered and then silently failed.
    /// </remarks>
    [Test]
    public void A_Targa_Decodes_Even_Though_Its_Bytes_Name_Another_Format()
    {
        var path = Path.Combine(_root, "swatch.tga");
        File.WriteAllBytes(path, TinyTarga);

        var image = FoileBrowser.Views.PreviewImage.Load(path, out var failure);

        Assert.That(image, Is.Not.Null, $"the Targa did not decode ({failure})");
    }

    /// <summary>A 2x2 uncompressed 24-bit Targa: the 18-byte header, then BGR triples.</summary>
    private static readonly byte[] TinyTarga =
    [
        0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x02, 0x00,
        0x18, 0x00,
        0x00, 0x00, 0xFF,  0x00, 0xFF, 0x00,
        0xFF, 0x00, 0x00,  0xFF, 0xFF, 0xFF,
    ];

    /// <summary>
    /// A picture keeps its preview even when its extension also names a container.
    /// </summary>
    /// <remarks>
    /// The rule the two panels split on: previewing shows the picture, entering shows the archive.
    /// Neither suppresses the other, and anything the app itself should not open is a job for Open
    /// With on the context menu.
    /// </remarks>
    [TestCase("dat")]
    [TestCase("img")]
    [TestCase("dll")]
    public void A_Picture_Named_As_A_Container_Still_Previews_As_A_Picture(string extension)
    {
        var name = "artwork." + extension;
        File.WriteAllBytes(Path.Combine(_root, name), OnePixelPng);

        var preview = new PreviewService().CreateAsync(FileEntry(name)).GetAwaiter().GetResult();

        Assert.That(preview.Kind, Is.EqualTo(PreviewKind.Image),
            $"a picture called .{extension} should still be shown as one");
    }

    /// <summary>
    /// A file holding several pictures shows all of them, not just the first.
    /// </summary>
    /// <remarks>
    /// 14 of the shipped formats can hold more than one — the pages of a TIFF or a PDF, the sizes in
    /// an .ico, the icons inside an executable's resources. The picture box already cycles frames,
    /// because that is how an animated GIF has always been shown, so they are handed over the same
    /// way. ImageMagick writes the fixture, and the test steps aside where it is not installed, which
    /// is the same bargain the disk-image tests make with genisoimage.
    /// </remarks>
    [TestCase("tif")]
    [TestCase("pdf")]
    public void A_File_Holding_Several_Pictures_Shows_Them_All(string extension)
    {
        var first = Path.Combine(_root, "a.png");
        var second = Path.Combine(_root, "b.png");
        var many = Path.Combine(_root, "pages." + extension);
        if (!Run("magick", "-size", "64x64", "xc:red", first) && !Run("convert", "-size", "64x64", "xc:red", first))
            Assert.Ignore("no ImageMagick on this machine");

        if (!Run("magick", "-size", "32x32", "xc:blue", second))
            Run("convert", "-size", "32x32", "xc:blue", second);
        if (!Run("magick", first, second, many) && !Run("convert", first, second, many))
            Assert.Ignore($"ImageMagick here cannot write a {extension}");

        var image = FoileBrowser.Views.PreviewImage.Load(many, out var failure);

        Assert.That(image, Is.Not.Null, $"the multi-page {extension} did not decode ({failure})");
        Assert.That(
            ((Hawkynt.NativeForms.Drawing.AnimatedImage)image!).FrameCount, Is.GreaterThan(1),
            $"both pages of the {extension} should be shown");
    }

    private static bool Run(string tool, params string[] args)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(tool)
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in args)
                start.ArgumentList.Add(argument);

            using var process = System.Diagnostics.Process.Start(start);
            if (process is null)
                return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---- archives and filesystem images (PRD §6.11) ----

    [Test]
    public void The_Archive_Registry_Really_Does_Ship_Its_Whole_Catalogue()
    {
        var listable = ListableDescriptors().ToList();

        TestContext.Out.WriteLine($"listable archive/filesystem formats: {listable.Count}");
        Assert.That(
            listable, Has.Count.GreaterThan(50),
            "a number this low means the generated registrations did not run");
    }

    [Test]
    public void Every_Listable_Format_Can_Be_Entered()
    {
        var archives = new ArchiveService();

        // One extension per format rather than all of them: a descriptor that claims an extension
        // another one also claims is a resolution question, not a coverage one, and it has its own
        // test below.
        var unreachable = ListableDescriptors()
            .Select(descriptor => (descriptor, extension: descriptor.Extensions.FirstOrDefault()))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.extension))
            .Where(pair => !archives.IsArchive("sample." + pair.extension!.TrimStart('.')))
            .Select(pair => $"{pair.descriptor.Id} (.{pair.extension!.TrimStart('.')})")
            .ToList();

        Assert.That(unreachable, Is.Empty, "these formats can be listed but double-clicking one would not enter it");
    }

    /// <summary>
    /// The ones the README and PRD name by hand, so the headline claim is checked and not just the
    /// registry's own self-consistency.
    /// </summary>
    [TestCase("zip")]
    [TestCase("tar")]
    [TestCase("7z")]
    [TestCase("rar")]
    [TestCase("cab")]
    [TestCase("cpio")]
    [TestCase("iso")]
    [TestCase("squashfs")]
    public void A_Named_Container_Is_Enterable(string extension)
    {
        Assert.That(new ArchiveService().IsArchive("sample." + extension), Is.True,
            $".{extension} is named in the docs as something you can walk into");
    }

    /// <summary>
    /// An extension both libraries claim resolves to something that can actually be entered.
    /// </summary>
    /// <remarks>
    /// Widening the preview to the whole image catalogue put the two registries in the same room:
    /// <c>.img</c> is a raster format to one and a disk image to the other. They do not collide in
    /// practice — the preview asks the image registry, entering asks the archive one — but a format
    /// that answers both and cannot be entered would be a regression worth hearing about.
    /// </remarks>
    [Test]
    public void An_Extension_Both_Libraries_Claim_Still_Opens_As_A_Container()
    {
        var archives = new ArchiveService();
        var pictures = ImageSupport.DecodableExtensions().ToHashSet(StringComparer.OrdinalIgnoreCase);

        var shared = ListableDescriptors()
            .SelectMany(descriptor => descriptor.Extensions)
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .Where(pictures.Contains)
            .Distinct()
            .ToList();

        TestContext.Out.WriteLine($"extensions claimed by both libraries: {string.Join(", ", shared)}");
        Assert.That(
            shared.Where(extension => !archives.IsArchive("sample." + extension)), Is.Empty,
            "an extension both libraries know should still enter as a container");
    }

    private static IEnumerable<IFormatDescriptor> ListableDescriptors()
    {
        // Entering an archive is what registers the formats, so ask something first.
        _ = new ArchiveService().IsArchive("sample.zip");
        return FormatRegistry.All.Where(d => d.Capabilities.HasFlag(FormatCapabilities.CanList));
    }

    private FileSystemEntry FileEntry(string name)
    {
        var path = Path.Combine(_root, name);
        return new FileSystemEntry
        {
            Name = name, FullPath = path, Kind = FileSystemEntryKind.File,
            Size = new FileInfo(path).Length,
        };
    }
}
