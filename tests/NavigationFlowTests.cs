using System.IO.Compression;
using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

/// <summary>
/// Walks the browsing flows end to end against a real directory tree and the real services, rather
/// than a fake filesystem: folders, history, the breadcrumb, favorites, drives and archives. The
/// per-piece tests elsewhere use fakes and prove each part in isolation; this one proves they still
/// work when wired to an actual disk, which is where path handling, hidden files and archive entry
/// tend to come apart.
/// </summary>
[TestFixture]
public class NavigationFlowTests
{
    private string _root = null!;
    private string _settingsFile = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Documents", "Reports"));
        Directory.CreateDirectory(Path.Combine(_root, "Pictures"));
        File.WriteAllText(Path.Combine(_root, "Documents", "notes.txt"), "notes");
        File.WriteAllText(Path.Combine(_root, "Documents", "Reports", "q1.txt"), "q1");
        File.WriteAllText(Path.Combine(_root, "readme.md"), "readme");
        File.WriteAllText(Path.Combine(_root, ".hidden"), "secret");
        _settingsFile = Path.Combine(_root, "settings.json");
    }

    [TearDown]
    public void TearDown()
    {
        _shells.DisposeAll();
        TempTree.Remove(_root);
    }

    private readonly ShellTracker _shells = new();

    /// <summary>
    /// Whether this process may actually read the volume. One can be mounted, listed in the sidebar
    /// and still be off limits — /boot/efi on the Linux runner is root-only — and navigating to it
    /// leaves the pane where it was, which read as the sidebar being broken rather than as the volume
    /// being someone else's.
    /// </summary>
    private static bool CanList(string path)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToList();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private FileTabViewModel NewTab() => new(new FileSystemService());

    private MainWindowViewModel NewShell()
    {
        var settings = new SettingsService(_settingsFile);
        return _shells.Track(new MainWindowViewModel(
            new FileSystemService(), new FileOperationService(), new TrashService(),
            new SearchService(), new PreviewService(), settings, new TagService(settings),
            new ShellService(), new ArchiveService()));
    }

    // ---- folders ----

    [Test]
    public async Task Lists_A_Real_Folder_With_Folders_First()
    {
        var tab = NewTab();

        await tab.NavigateToAsync(_root);

        var names = tab.Entries.Select(e => e.Name).ToList();
        Assert.That(names, Does.Contain("Documents"));
        Assert.That(names, Does.Contain("readme.md"));
        Assert.That(names, Does.Not.Contain(".hidden"), "hidden entries are off by default");
        Assert.That(
            names.IndexOf("Documents"), Is.LessThan(names.IndexOf("readme.md")),
            "folders sort ahead of files");
    }

    [Test]
    public async Task Shows_Hidden_Entries_When_Asked()
    {
        var tab = NewTab();
        await tab.NavigateToAsync(_root);

        tab.ShowHidden = true;
        await tab.RefreshCommand.ExecuteAsync(null);

        Assert.That(tab.Entries.Select(e => e.Name), Does.Contain(".hidden"));
    }

    [Test]
    public async Task Opens_A_Folder_Then_Walks_Up_Back_And_Forward()
    {
        var tab = NewTab();
        await tab.NavigateToAsync(_root);

        var documents = tab.Entries.First(e => e.Name == "Documents");
        await tab.OpenCommand.ExecuteAsync(documents);
        Assert.That(tab.CurrentPath, Is.EqualTo(Path.Combine(_root, "Documents")));
        Assert.That(tab.Entries.Select(e => e.Name), Does.Contain("notes.txt"));

        await tab.GoUpCommand.ExecuteAsync(null);
        Assert.That(tab.CurrentPath, Is.EqualTo(_root));

        await tab.GoBackCommand.ExecuteAsync(null);
        Assert.That(tab.CurrentPath, Is.EqualTo(Path.Combine(_root, "Documents")), "back returns to the folder");

        await tab.GoForwardCommand.ExecuteAsync(null);
        Assert.That(tab.CurrentPath, Is.EqualTo(_root), "forward returns to where back left");
    }

    [Test]
    public async Task Breadcrumb_Jumps_Back_Up_The_Real_Path()
    {
        var tab = NewTab();
        await tab.NavigateToAsync(Path.Combine(_root, "Documents", "Reports"));

        var segment = tab.Breadcrumbs.First(b => b.Name == "Documents");
        await tab.NavigateBreadcrumbCommand.ExecuteAsync(segment);

        Assert.That(tab.CurrentPath, Is.EqualTo(Path.Combine(_root, "Documents")));
    }

    [Test]
    public async Task Filtering_Narrows_The_Listing_Without_Leaving_The_Folder()
    {
        var tab = NewTab();
        await tab.NavigateToAsync(_root);

        tab.FilterText = "read";

        Assert.That(tab.Entries.Select(e => e.Name), Is.EquivalentTo(new[] { "readme.md" }));
        Assert.That(tab.CurrentPath, Is.EqualTo(_root));
    }

    // ---- archives ----

    [Test]
    public async Task Enters_A_Real_Archive_Descends_And_Comes_Back_Out()
    {
        var zip = Path.Combine(_root, "bundle.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(archive.CreateEntry("top.txt").Open()))
                w.Write("top");
            using (var w = new StreamWriter(archive.CreateEntry("inner/deep.txt").Open()))
                w.Write("deep");
        }

        var tab = NewTab();
        await tab.NavigateToAsync(_root);

        var entry = tab.Entries.First(e => e.Name == "bundle.zip");
        await tab.OpenCommand.ExecuteAsync(entry);
        Assert.That(tab.Entries.Select(e => e.Name), Is.EquivalentTo(new[] { "inner", "top.txt" }),
            "the archive lists as a folder");

        var inner = tab.Entries.First(e => e.Name == "inner");
        await tab.OpenCommand.ExecuteAsync(inner);
        Assert.That(tab.Entries.Select(e => e.Name), Is.EquivalentTo(new[] { "deep.txt" }));

        await tab.GoUpCommand.ExecuteAsync(null);
        Assert.That(tab.Entries.Select(e => e.Name), Is.EquivalentTo(new[] { "inner", "top.txt" }));

        await tab.GoUpCommand.ExecuteAsync(null);
        Assert.That(tab.CurrentPath, Is.EqualTo(_root), "leaving the archive lands back on disk");
    }

    // ---- sidebar: favorites and drives ----

    [Test]
    public async Task Pinning_The_Current_Folder_Adds_A_Favorite_That_Navigates()
    {
        var shell = NewShell();
        await shell.InitializeAsync();
        await shell.ActiveTab!.NavigateToAsync(Path.Combine(_root, "Pictures"));

        await shell.PinFavoriteCommand.ExecuteAsync(null);

        var favorites = shell.Sections.Single(s => s.Id == "favorites");
        var pinned = favorites.Items.FirstOrDefault(i => i.Path == Path.Combine(_root, "Pictures"));
        Assert.That(pinned, Is.Not.Null, "the folder was pinned");

        await shell.ActiveTab.NavigateToAsync(_root);
        await shell.ActiveTab.OpenSidebarItemCommand.ExecuteAsync(pinned);

        Assert.That(shell.ActiveTab.CurrentPath, Is.EqualTo(Path.Combine(_root, "Pictures")),
            "opening the favorite navigates this pane");
    }

    [Test]
    public async Task The_Sidebar_Lists_Real_Drives_That_Navigate()
    {
        var shell = NewShell();

        await shell.InitializeAsync();

        var drives = shell.Sections.Single(s => s.Id == "drives");
        var navigable = drives.Items.Where(i => i.IsNavigable && Directory.Exists(i.Path) && CanList(i.Path)).ToList();
        if (navigable.Count == 0)
            Assert.Ignore("no volume this process is allowed to list");

        await shell.ActiveTab!.OpenSidebarItemCommand.ExecuteAsync(navigable[0]);
        Assert.That(shell.ActiveTab.CurrentPath, Is.EqualTo(navigable[0].Path));
    }

    // ---- undo ----

    [Test]
    public async Task Renaming_A_Real_File_Can_Be_Undone_And_Redone()
    {
        var shell = NewShell();
        await shell.InitializeAsync();
        await shell.ActiveTab!.NavigateToAsync(Path.Combine(_root, "Documents"));
        shell.ActiveTab.SelectedEntry = shell.ActiveTab.Entries.First(e => e.Name == "notes.txt");
        shell.NameRequester = _ => Task.FromResult<string?>("renamed.txt");

        await shell.RenameSelectedCommand.ExecuteAsync(null);
        Assert.That(File.Exists(Path.Combine(_root, "Documents", "renamed.txt")), Is.True, "the rename happened");
        Assert.That(shell.Undo.CanUndo, Is.True);
        Assert.That(shell.Undo.UndoDescription, Is.EqualTo("Rename notes.txt"));

        await shell.UndoLastCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_root, "Documents", "notes.txt")), Is.True, "the old name is back");
            Assert.That(File.Exists(Path.Combine(_root, "Documents", "renamed.txt")), Is.False);
            Assert.That(shell.Undo.CanRedo, Is.True);
        });

        await shell.RedoLastCommand.ExecuteAsync(null);
        Assert.That(File.Exists(Path.Combine(_root, "Documents", "renamed.txt")), Is.True, "redo repeats it");
    }

    [Test]
    public async Task Moving_Real_Files_To_The_Other_Pane_Can_Be_Undone()
    {
        var shell = NewShell();
        await shell.InitializeAsync();
        await shell.AddPaneCommand.ExecuteAsync(null);              // a destination pane
        await shell.ActiveTab!.NavigateToAsync(Path.Combine(_root, "Pictures"));
        shell.ActivateTab(shell.Tabs[0]);
        await shell.ActiveTab!.NavigateToAsync(Path.Combine(_root, "Documents"));
        shell.ActiveTab.SelectedEntry = shell.ActiveTab.Entries.First(e => e.Name == "notes.txt");
        shell.ActiveTab.SetSelection([shell.ActiveTab.SelectedEntry]);

        shell.MoveToOtherCommand.Execute(null);
        var moved = Path.Combine(_root, "Pictures", "notes.txt");
        Assert.That(await WaitUntilAsync(() => File.Exists(moved)), Is.True, "the move completed");
        Assert.That(await WaitUntilAsync(() => shell.Undo.CanUndo), Is.True, "and was recorded");

        await shell.UndoLastCommand.ExecuteAsync(null);

        Assert.That(
            await WaitUntilAsync(() => File.Exists(Path.Combine(_root, "Documents", "notes.txt"))),
            Is.True, "undo moved it home");
        Assert.That(File.Exists(moved), Is.False);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 4000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(20);
        }

        return condition();
    }

    // ---- a folder that is not there any more ----

    [Test]
    public async Task Navigating_Somewhere_That_Does_Not_Exist_Leaves_The_Tab_Usable()
    {
        var tab = NewTab();
        await tab.NavigateToAsync(_root);

        await tab.NavigateToAsync(Path.Combine(_root, "no-such-folder"));

        Assert.That(tab.Entries, Is.Not.Null, "the tab did not fall over");
        await tab.NavigateToAsync(_root);
        Assert.That(tab.Entries.Select(e => e.Name), Does.Contain("Documents"), "and still navigates afterwards");
    }
}
