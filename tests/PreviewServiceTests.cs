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
        TempTree.Remove(_root);
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

    /// <summary>
    /// One file a person actually selected still earns the picture panel with its own bytes, however
    /// ordinary its name.
    /// </summary>
    /// <remarks>
    /// The gallery sweep stopped trusting <c>.cs</c> and its three siblings, because a checkout is
    /// full of them and none of them is an Atari screen. That is a rule about sweeping, not about
    /// picture-ness: pointing at a single file is still worth the read, and a picture called
    /// <c>.cs</c> still opens as one.
    /// </remarks>
    [Test]
    public async Task A_Picture_With_A_Source_Files_Name_Still_Previews_As_A_Picture()
    {
        await System.IO.File.WriteAllBytesAsync(
            Path.Combine(_root, "screen.cs"), ImageFixture.Encode(".png", 8, 8));

        var result = await _preview.CreateAsync(FileEntry("screen.cs"));

        Assert.That(result.Kind, Is.EqualTo(PreviewKind.Image));
    }

    /// <summary>And a source file called what it is stays text.</summary>
    [Test]
    public async Task A_Source_File_Previews_As_Text()
    {
        await System.IO.File.WriteAllTextAsync(
            Path.Combine(_root, "Program.cs"), "static void Main() { }\n");

        var result = await _preview.CreateAsync(FileEntry("Program.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(PreviewKind.Text));
            Assert.That(result.Text, Does.Contain("Main"));
        });
    }
}
