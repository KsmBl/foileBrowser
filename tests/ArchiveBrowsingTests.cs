using System.IO.Compression;
using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class ArchiveBrowsingTests
{
    private string _root = null!;

    /// <summary>
    /// What the first crumb of a real path is called. "/" on POSIX, but a drive letter on Windows —
    /// which is what these assertions used to spell out, so they failed on that runner alone.
    /// </summary>
    private static string FilesystemRoot => Path.GetPathRoot(Path.GetTempPath())!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-arcbrowse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        TempTree.Remove(_root);
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

    // ---- the path shown while inside an archive (PRD §6.11) ----

    [Test]
    public async Task An_Archives_Crumbs_Start_At_The_Real_Folder_It_Lives_In()
    {
        // Inside an archive the trail used to begin at the archive file, so the folders it lives in
        // were unreachable — there was nothing to click to get back out.
        var zip = MakeZip();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);

        await tab.OpenCommand.ExecuteAsync(Entry(zip));

        var names = tab.Breadcrumbs.Select(c => c.Name).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("sample.zip"));
            Assert.That(names.IndexOf("sample.zip"), Is.GreaterThan(0), "the folders above it come first");
            Assert.That(names[0], Is.EqualTo(FilesystemRoot), "the trail reaches the filesystem root");
        });
    }

    [Test]
    public async Task Clicking_A_Folder_Crumb_Leaves_The_Archive()
    {
        var zip = MakeZip();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);
        await tab.OpenCommand.ExecuteAsync(Entry(zip));

        var folder = tab.Breadcrumbs.Last(c => c.Name != "sample.zip");
        await tab.NavigateBreadcrumbCommand.ExecuteAsync(folder);

        Assert.Multiple(() =>
        {
            Assert.That(tab.CurrentPath, Is.EqualTo(_root));
            Assert.That(tab.Entries.Select(e => e.Name), Does.Contain("sample.zip"), "listing the real folder again");
        });
    }

    [Test]
    public async Task Inside_A_Subfolder_The_Crumbs_Show_Both_Halves_Of_The_Path()
    {
        var zip = MakeZip();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);
        await tab.OpenCommand.ExecuteAsync(Entry(zip));
        await tab.OpenCommand.ExecuteAsync(tab.Entries.Single(e => e.Name == "sub"));

        var names = tab.Breadcrumbs.Select(c => c.Name).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(names[^1], Is.EqualTo("sub"), "the folder inside the archive");
            Assert.That(names[^2], Is.EqualTo("sample.zip"));
            Assert.That(names, Does.Contain(FilesystemRoot), "and still the real path above it");
        });
    }

    [Test]
    public async Task The_Crumbs_Compose_Back_Into_The_Path_The_Tab_Reports()
    {
        // What the path bar seeds its editable field with. It has to be the real path, or Ctrl+L
        // inside an archive offers something that is not a path at all.
        var zip = MakeZip();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);
        await tab.OpenCommand.ExecuteAsync(Entry(zip));
        await tab.OpenCommand.ExecuteAsync(tab.Entries.Single(e => e.Name == "sub"));

        // Not the captions joined back together: inside an archive the path is a filesystem path as
        // far as the archive file and the archive's own entry names below it, each with its own
        // convention, so on Windows no single separator reproduces it. The pane seeds the field from
        // the tab (Breadcrumb.PathComposer), and what has to hold is that the trail reaches the whole
        // way down — a crumb per level, the deepest carrying exactly the path the tab is showing.
        Assert.Multiple(() =>
        {
            Assert.That(tab.Breadcrumbs, Is.Not.Empty);
            Assert.That(tab.Breadcrumbs[^1].Path, Is.EqualTo(tab.CurrentPath));
            Assert.That(tab.Breadcrumbs.Select(c => c.Path), Is.Unique);
        });
    }

    [Test]
    public async Task Clicking_The_Archive_Crumb_Returns_To_Its_Root()
    {
        var zip = MakeZip();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);
        await tab.OpenCommand.ExecuteAsync(Entry(zip));
        await tab.OpenCommand.ExecuteAsync(tab.Entries.Single(e => e.Name == "sub"));

        var archive = tab.Breadcrumbs.Single(c => c.Name == "sample.zip");
        await tab.NavigateBreadcrumbCommand.ExecuteAsync(archive);

        Assert.That(tab.Entries.Select(e => e.Name), Is.EquivalentTo(new[] { "sub", "hello.txt" }));
    }
}
