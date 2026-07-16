using System.IO.Compression;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class ArchiveServiceTests
{
    private string _root = null!;
    private ArchiveService _archives = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-arc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _archives = new ArchiveService();
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string MakeZip()
    {
        var zip = Path.Combine(_root, "sample.zip");
        using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
        using (var w = new StreamWriter(z.CreateEntry("hello.txt").Open())) w.Write("hello world");
        using (var w = new StreamWriter(z.CreateEntry("sub/deep.txt").Open())) w.Write("deep content");
        return zip;
    }

    [Test]
    public void IsArchive_Recognises_Zip_And_Rejects_Text()
    {
        Assert.That(_archives.IsArchive("/x/file.zip"), Is.True);
        Assert.That(_archives.IsArchive("/x/notes.txt"), Is.False);
    }

    [Test]
    public void Identify_Names_The_Format()
    {
        Assert.That(_archives.Identify("/x/file.zip"), Is.Not.Null.And.Contain("ZIP").IgnoreCase);
    }

    [Test]
    public async Task List_Returns_Archive_Entries()
    {
        var zip = MakeZip();

        var entries = await _archives.ListAsync(zip);

        Assert.That(entries.Select(e => e.Name), Does.Contain("hello.txt"));
        var hello = entries.First(e => e.Name == "hello.txt");
        Assert.That(hello.Size, Is.EqualTo(11));
        Assert.That(hello.IsDirectory, Is.False);
    }

    [Test]
    public async Task ExtractEntry_Writes_A_Single_File_Streamed()
    {
        var zip = MakeZip();
        var dest = Path.Combine(_root, "out", "deep.txt");

        await _archives.ExtractEntryAsync(zip, "sub/deep.txt", dest);

        Assert.That(File.Exists(dest), Is.True);
        Assert.That(await File.ReadAllTextAsync(dest), Is.EqualTo("deep content"));
    }

    [Test]
    public async Task ExtractAll_Writes_Files_To_Disk()
    {
        var zip = MakeZip();
        var dest = Path.Combine(_root, "out");

        await _archives.ExtractAllAsync(zip, dest);

        Assert.That(await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")), Is.EqualTo("hello world"));
        Assert.That(File.Exists(Path.Combine(dest, "sub", "deep.txt")), Is.True);
    }

    [Test]
    public void Listing_A_NonArchive_Throws()
    {
        Assert.ThrowsAsync<NotSupportedException>(() => _archives.ListAsync("/x/plain.txt"));
    }
}
