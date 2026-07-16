using System.IO.Compression;
using FoileBrowser.Models;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class ArchiveBrowsingTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-arcbrowse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
        using (var w = new StreamWriter(z.CreateEntry("hello.txt").Open())) w.Write("hi");
        using (var w = new StreamWriter(z.CreateEntry("sub/deep.txt").Open())) w.Write("deep");
        return zip;
    }

    private static FileEntryViewModel Entry(string path) =>
        new(new FileSystemEntry { Name = Path.GetFileName(path), FullPath = path, Kind = FileSystemEntryKind.File });

    [Test]
    public async Task Entering_Archive_Lists_Top_Level_Virtually()
    {
        var zip = MakeZip();
        var tab = new FileTabViewModel(new FakeFileSystem());

        await tab.OpenCommand.ExecuteAsync(Entry(zip));

        Assert.That(tab.Entries.Select(e => e.Name), Is.EquivalentTo(new[] { "sub", "hello.txt" }));
        Assert.That(tab.Entries.First(e => e.Name == "sub").IsDirectory, Is.True);
        // Nothing was extracted to disk — only the archive index was read.
        Assert.That(Directory.EnumerateFileSystemEntries(_root).Count(), Is.EqualTo(1), "only the zip exists");
    }

    [Test]
    public async Task Descending_Into_Archive_Folder_Lists_Its_Entries()
    {
        var zip = MakeZip();
        var tab = new FileTabViewModel(new FakeFileSystem());
        await tab.OpenCommand.ExecuteAsync(Entry(zip));

        var sub = tab.Entries.First(e => e.Name == "sub");
        await tab.OpenCommand.ExecuteAsync(sub);

        Assert.That(tab.Entries.Select(e => e.Name), Is.EquivalentTo(new[] { "deep.txt" }));

        // Going up returns to the archive root.
        await tab.GoUpCommand.ExecuteAsync(null);
        Assert.That(tab.Entries.Select(e => e.Name), Does.Contain("hello.txt"));
    }
}
