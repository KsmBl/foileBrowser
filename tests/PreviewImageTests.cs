using FoileBrowser.Views;
using SkiaSharp;

namespace FoileBrowser.Tests;

/// <summary>
/// Covers the inspector's image decoding (PRD §6.5): the toolkit's own decoder for the formats it
/// reads, SkiaSharp for the rest, a size cap on both, and a clear verdict when neither works.
/// </summary>
[TestFixture]
public class PreviewImageTests
{
    private readonly List<string> _temporary = [];

    [TearDown]
    public void Cleanup()
    {
        foreach (var path in _temporary)
            File.Delete(path);
        _temporary.Clear();
    }

    [Test]
    public void Decodes_A_Png_Without_Skia()
    {
        var path = this.Write(SKEncodedImageFormat.Png, 40, 25);

        var image = PreviewImage.Load(path, out var failure);

        Assert.That(image, Is.Not.Null);
        Assert.That(failure, Is.EqualTo(PreviewImage.Failure.None));
        Assert.Multiple(() =>
        {
            Assert.That(image!.Width, Is.EqualTo(40));
            Assert.That(image.Height, Is.EqualTo(25));
        });
    }

    [Test]
    public void Decodes_A_Jpeg()
    {
        // The toolkit decodes JPEG itself now; before that this was the SkiaSharp fallback path.
        // Either way the inspector has to end up with the right pixels.
        var path = this.Write(SKEncodedImageFormat.Jpeg, 64, 48);

        var image = PreviewImage.Load(path, out var failure);

        Assert.That(image, Is.Not.Null);
        Assert.That(failure, Is.EqualTo(PreviewImage.Failure.None));
        Assert.Multiple(() =>
        {
            Assert.That(image!.Width, Is.EqualTo(64));
            Assert.That(image.Height, Is.EqualTo(48));
        });
    }

    [Test]
    public void Scales_An_Oversized_Image_Down_To_The_Preview_Cap()
    {
        var path = this.Write(SKEncodedImageFormat.Png, 5000, 1000);

        var image = PreviewImage.Load(path, out var failure);

        Assert.That(image, Is.Not.Null);
        Assert.That(failure, Is.EqualTo(PreviewImage.Failure.None));
        Assert.Multiple(() =>
        {
            Assert.That(image!.Width, Is.EqualTo(2048), "the longest edge is capped");
            Assert.That(image.Height, Is.EqualTo(409), "the aspect ratio is kept");
        });
    }

    [Test]
    public void A_Png_Header_Claiming_A_Huge_Image_Is_Refused_Rather_Than_Allocated()
    {
        // A decompression bomb: eight bytes of signature and an IHDR declaring 60000×60000 (14 GB as
        // ARGB). The toolkit's decoder sizes its buffer straight from this, so it must never see it.
        var header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(new byte[8])
            .Concat(new byte[] { 0x00, 0x00, 0xEA, 0x60, 0x00, 0x00, 0xEA, 0x60 })
            .Concat(new byte[16])
            .ToArray();
        var path = Path.Combine(Path.GetTempPath(), "foile-preview-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, header);
        _temporary.Add(path);

        var image = PreviewImage.Load(path, out var failure);

        Assert.That(image, Is.Null);
        Assert.That(failure, Is.Not.EqualTo(PreviewImage.Failure.None));
    }

    [Test]
    public void Reports_A_File_That_Is_Not_An_Image()
    {
        var path = Path.Combine(Path.GetTempPath(), "foile-preview-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllText(path, "this is not an image at all");
        _temporary.Add(path);

        var image = PreviewImage.Load(path, out var failure);

        Assert.That(image, Is.Null);
        Assert.That(failure, Is.EqualTo(PreviewImage.Failure.Unreadable));
    }

    [Test]
    public void Reports_A_Missing_File()
    {
        var image = PreviewImage.Load(Path.Combine(Path.GetTempPath(), "foile-does-not-exist.png"), out var failure);

        Assert.That(image, Is.Null);
        Assert.That(failure, Is.EqualTo(PreviewImage.Failure.Unreadable));
    }

    private string Write(SKEncodedImageFormat format, int width, int height)
    {
        var extension = format == SKEncodedImageFormat.Png ? ".png" : ".jpg";
        var path = Path.Combine(Path.GetTempPath(), "foile-preview-" + Guid.NewGuid().ToString("N") + extension);

        using (var bitmap = new SKBitmap(width, height))
        {
            bitmap.Erase(SKColors.CornflowerBlue);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, 90);
            File.WriteAllBytes(path, data.ToArray());
        }

        _temporary.Add(path);
        return path;
    }
}
