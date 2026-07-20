using FoileBrowser.Services;
using SkiaSharp;

namespace FoileBrowser.Tests;

[TestFixture]
public class MetadataServiceTests
{
    [Test]
    public void Provides_The_Image_And_Media_Columns()
    {
        var ids = new MetadataService().Columns.Select(c => c.Id).ToList();
        Assert.That(ids, Does.Contain("img.dimensions"));
        Assert.That(ids, Does.Contain("img.channels"));
        Assert.That(ids, Does.Contain("av.fps"));
    }

    [Test]
    public void Non_Image_File_Yields_Blank_For_An_Image_Column()
    {
        var svc = new MetadataService();
        Assert.That(svc.Get("/x/notes.txt", "img.dimensions", () => { }), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Reads_Image_Dimensions_Via_Skia()
    {
        var svc = new MetadataService();
        var path = Path.Combine(Path.GetTempPath(), "foile-meta-" + Guid.NewGuid().ToString("N") + ".png");
        using (var bitmap = new SKBitmap(7, 3))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            File.WriteAllBytes(path, data.ToArray());

        try
        {
            using var ready = new ManualResetEventSlim();
            var first = svc.Get(path, "img.dimensions", ready.Set);
            Assert.That(first, Is.EqualTo("…"), "returns a pending marker while computing");
            Assert.That(ready.Wait(TimeSpan.FromSeconds(5)), Is.True, "metadata finished computing");

            Assert.That(svc.Get(path, "img.dimensions", () => { }), Is.EqualTo("7×3"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
