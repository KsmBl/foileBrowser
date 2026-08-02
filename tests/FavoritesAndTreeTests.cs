using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

/// <summary>
/// Building up a set of favourites, and walking the sidebar tree into sub-folders (PRD §6.2) — the
/// parts of a workflow that only pay off if they are quick.
/// </summary>
[TestFixture]
public class FavoritesAndTreeTests
{
    private string _settingsDir = null!;
    private string _settingsFile = null!;
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsDir = Path.Combine(Path.GetTempPath(), "foile-favs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_settingsDir);
        _settingsFile = Path.Combine(_settingsDir, "settings.json");
        _root = Directory.CreateDirectory(Path.Combine(_settingsDir, "tree")).FullName;
    }

    [TearDown]
    public void TearDown()
    {
        _shells.DisposeAll();
        TempTree.Remove(_settingsDir);
    }

    private readonly ShellTracker _shells = new();

    private MainWindowViewModel CreateShell()
    {
        var settings = new SettingsService(_settingsFile);
        return _shells.Track(new MainWindowViewModel(new FileSystemService(), new FileOperationService(),
            new RecordingTrash(), new SearchService(), new PreviewService(), settings, new TagService(settings),
            new ShellService()));
    }

    private static IEnumerable<SidebarItemViewModel> Favorites(MainWindowViewModel vm)
        => vm.Sections.Where(s => s.Id == "favorites").SelectMany(s => s.Items);

    /// <summary>
    /// Polls until <paramref name="settled"/> holds. A node fills its children on a worker and hands
    /// the additions to the synchronization context it was built on; headless there is none, so they
    /// land on that worker instead and a poll can read the collection halfway through an add. That is
    /// the harness having no UI thread rather than anything being wrong, so a torn read counts as
    /// "not settled yet" and the next tick looks again.
    /// </summary>
    private static async Task<bool> UntilAsync(Func<bool> settled)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (Settled())
                return true;
            await Task.Delay(15);
        }

        return Settled();

        bool Settled()
        {
            try
            {
                return settled();
            }
            catch (InvalidOperationException)
            {
                return false; // collection changed under the read; look again next tick
            }
        }
    }

    // ---- favourites ----

    [Test]
    public async Task Any_Folder_Can_Be_Pinned_Not_Only_The_One_Being_Looked_At()
    {
        var elsewhere = Directory.CreateDirectory(Path.Combine(_root, "elsewhere")).FullName;
        var vm = CreateShell();
        await vm.InitializeAsync();

        await vm.PinPathCommand.ExecuteAsync(elsewhere);

        Assert.That(Favorites(vm).Select(f => f.Path), Does.Contain(elsewhere));
    }

    [Test]
    public async Task Pinning_The_Selected_Folder_Pins_That_One_Rather_Than_The_Current_One()
    {
        var child = Directory.CreateDirectory(Path.Combine(_root, "child")).FullName;
        var vm = CreateShell();
        await vm.InitializeAsync();
        await vm.ActiveTab!.NavigateToAsync(_root);
        vm.ActiveTab.SelectedEntry = vm.ActiveTab.Entries.Single(e => e.Name == "child");

        await vm.PinSelectedCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(Favorites(vm).Select(f => f.Path), Does.Contain(child));
            Assert.That(Favorites(vm).Select(f => f.Path), Does.Not.Contain(_root), "not where we stand");
        });
    }

    [Test]
    public async Task Pinning_A_File_Or_A_Missing_Folder_Does_Nothing()
    {
        var file = Path.Combine(_root, "note.txt");
        await System.IO.File.WriteAllTextAsync(file, "x");
        var vm = CreateShell();
        await vm.InitializeAsync();
        var before = Favorites(vm).Count();

        await vm.PinPathCommand.ExecuteAsync(file);
        await vm.PinPathCommand.ExecuteAsync(Path.Combine(_root, "never-existed"));

        Assert.That(Favorites(vm).Count(), Is.EqualTo(before));
    }

    [Test]
    public async Task Pinning_The_Same_Folder_Twice_Leaves_One_Row()
    {
        var vm = CreateShell();
        await vm.InitializeAsync();

        await vm.PinPathCommand.ExecuteAsync(_root);
        await vm.PinPathCommand.ExecuteAsync(_root);

        Assert.That(Favorites(vm).Count(f => f.Path == _root), Is.EqualTo(1));
    }

    [Test]
    public async Task A_Pinned_Folder_Can_Be_Unpinned_Again()
    {
        var vm = CreateShell();
        await vm.InitializeAsync();
        await vm.PinPathCommand.ExecuteAsync(_root);
        var pinned = Favorites(vm).Single(f => f.Path == _root);

        await vm.UnpinFavoriteCommand.ExecuteAsync(pinned);

        Assert.That(Favorites(vm).Select(f => f.Path), Does.Not.Contain(_root));
    }

    [Test]
    public async Task Favourites_Survive_A_Restart()
    {
        var first = CreateShell();
        await first.InitializeAsync();
        await first.PinPathCommand.ExecuteAsync(_root);

        var second = CreateShell();
        await second.InitializeAsync();

        Assert.That(Favorites(second).Select(f => f.Path), Does.Contain(_root));
    }

    // ---- the sidebar tree ----

    [Test]
    public async Task Expanding_A_Tree_Node_Lists_Its_Sub_Folders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        Directory.CreateDirectory(Path.Combine(_root, "beta"));

        var node = new FolderNodeViewModel("tree", _root);
        node.IsExpanded = true;

        // Waiting on the names, not a count: a fresh node already holds one placeholder child so the
        // expander arrow appears before anything has been read from disk.
        await UntilAsync(() => node.Children.Any(c => c.Name == "alpha"));
        Assert.That(node.Children.Select(c => c.Name), Is.EquivalentTo(new[] { "alpha", "beta" }));
    }

    [Test]
    public async Task A_Sub_Folder_Expands_In_Turn_So_The_Tree_Goes_As_Deep_As_It_Needs()
    {
        var alpha = Directory.CreateDirectory(Path.Combine(_root, "alpha")).FullName;
        Directory.CreateDirectory(Path.Combine(alpha, "inner"));

        var node = new FolderNodeViewModel("tree", _root);
        node.IsExpanded = true;
        await UntilAsync(() => node.Children.Any(c => c.Name == "alpha"));

        var child = node.Children.Single(c => c.Name == "alpha");
        child.IsExpanded = true;
        await UntilAsync(() => child.Children.Any(c => c.Name == "inner"));

        Assert.That(child.Children.Select(c => c.Name), Is.EquivalentTo(new[] { "inner" }));
    }

    [Test]
    public async Task A_Folder_With_Nothing_In_It_Expands_To_Nothing_Rather_Than_Hanging()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;

        var node = new FolderNodeViewModel("empty", empty);
        node.IsExpanded = true;
        await Task.Delay(100);

        Assert.That(node.Children, Is.Empty);
    }
}
