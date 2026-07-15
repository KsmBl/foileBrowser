using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class PreviewServiceTests
{
    private string _root = null!;
    private PreviewService _preview = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _preview = new PreviewService();
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
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

    [Test]
    public async Task Text_File_Yields_Text_Preview()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "note.txt"), "hello preview");

        var result = await _preview.CreateAsync(FileEntry("note.txt"));

        Assert.That(result.Kind, Is.EqualTo(PreviewKind.Text));
        Assert.That(result.Text, Does.Contain("hello preview"));
    }

    [Test]
    public async Task Image_Extension_Yields_Image_Preview()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "pic.png"), [1, 2, 3]);

        var result = await _preview.CreateAsync(FileEntry("pic.png"));

        Assert.That(result.Kind, Is.EqualTo(PreviewKind.Image));
        Assert.That(result.ImagePath, Does.EndWith("pic.png"));
    }

    [Test]
    public async Task Binary_File_Yields_No_Text()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "blob.dat"), [0, 1, 0, 2, 0, 3]);

        var result = await _preview.CreateAsync(FileEntry("blob.dat"));

        Assert.That(result.Kind, Is.EqualTo(PreviewKind.None));
    }

    [Test]
    public async Task Directory_Yields_Folder_Summary()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "folder")).FullName;
        await File.WriteAllTextAsync(Path.Combine(dir, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        var entry = new FileSystemEntry { Name = "folder", FullPath = dir, Kind = FileSystemEntryKind.Directory };

        var result = await _preview.CreateAsync(entry);

        Assert.That(result.Kind, Is.EqualTo(PreviewKind.Folder));
        Assert.That(result.Info, Does.Contain("1 folders"));
        Assert.That(result.Info, Does.Contain("1 files"));
    }
}
