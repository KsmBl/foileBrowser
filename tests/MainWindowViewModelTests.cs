using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class MainWindowViewModelTests
{
    /// <summary>In-memory filesystem returning a fixed listing for every path.</summary>
    private sealed class FakeFileSystem : IFileSystemService
    {
        public List<FileSystemEntry> Entries { get; } = [];
        public string? ParentOverride { get; set; } = "/parent";

        public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(
            string path, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileSystemEntry>>(Entries.ToList());

        public Task<IReadOnlyList<FileSystemEntry>> ListDrivesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileSystemEntry>>([]);

        public string? GetParent(string path) => ParentOverride;

        public bool DirectoryExists(string path) => true;
    }

    private static FileSystemEntry Dir(string name) => new()
    {
        Name = name, FullPath = "/x/" + name, Kind = FileSystemEntryKind.Directory,
    };

    private static FileSystemEntry FileEntry(string name, bool hidden = false) => new()
    {
        Name = name, FullPath = "/x/" + name, Kind = FileSystemEntryKind.File, Size = 1, IsHidden = hidden,
    };

    private static async Task<MainWindowViewModel> CreateAndLoadAsync(FakeFileSystem fs)
    {
        var vm = new MainWindowViewModel(fs);
        await vm.NavigateToAsync("/x");
        return vm;
    }

    [Test]
    public async Task Navigate_Populates_Entries_Sorted_Folders_First()
    {
        var fs = new FakeFileSystem();
        fs.Entries.AddRange([FileEntry("b.txt"), Dir("zeta"), FileEntry("a.txt"), Dir("alpha")]);

        var vm = await CreateAndLoadAsync(fs);

        Assert.That(vm.Entries.Select(e => e.Name), Is.EqualTo(new[] { "alpha", "zeta", "a.txt", "b.txt" }));
        Assert.That(vm.CurrentPath, Is.EqualTo("/x"));
    }

    [Test]
    public async Task Hidden_Entries_Are_Filtered_Until_Toggled()
    {
        var fs = new FakeFileSystem();
        fs.Entries.AddRange([FileEntry("visible.txt"), FileEntry(".secret", hidden: true)]);

        var vm = await CreateAndLoadAsync(fs);
        Assert.That(vm.Entries.Select(e => e.Name), Does.Not.Contain(".secret"));

        vm.ShowHidden = true;
        Assert.That(vm.Entries.Select(e => e.Name), Does.Contain(".secret"));
    }

    [Test]
    public async Task SortByCommand_Toggles_Direction_On_Repeat()
    {
        var fs = new FakeFileSystem();
        fs.Entries.AddRange([Dir("alpha"), Dir("zeta")]);
        var vm = await CreateAndLoadAsync(fs);

        // Switching to a different column, then back to Name, resets to ascending.
        vm.SortByCommand.Execute(SortColumn.Size);
        vm.SortByCommand.Execute(SortColumn.Name);
        Assert.That(vm.SortDirection, Is.EqualTo(SortDirection.Ascending));
        Assert.That(vm.Entries.Select(e => e.Name), Is.EqualTo(new[] { "alpha", "zeta" }));

        // Clicking the already-sorted column flips direction.
        vm.SortByCommand.Execute(SortColumn.Name);
        Assert.That(vm.SortDirection, Is.EqualTo(SortDirection.Descending));
        Assert.That(vm.Entries.Select(e => e.Name), Is.EqualTo(new[] { "zeta", "alpha" }));
    }

    [Test]
    public async Task Navigation_History_Enables_Back_After_Second_Navigation()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(Dir("child"));
        var vm = await CreateAndLoadAsync(fs);

        Assert.That(vm.CanGoBack, Is.False);

        await vm.NavigateToAsync("/x/child");
        Assert.That(vm.CanGoBack, Is.True);

        await vm.GoBackCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentPath, Is.EqualTo("/x"));
    }

    [Test]
    public async Task CanGoUp_Reflects_Parent_Availability()
    {
        var fs = new FakeFileSystem { ParentOverride = "/parent" };
        var vm = await CreateAndLoadAsync(fs);
        Assert.That(vm.CanGoUp, Is.True);

        fs.ParentOverride = null; // at a root
        await vm.NavigateToAsync("/root");
        Assert.That(vm.CanGoUp, Is.False);
    }

    [Test]
    public async Task StatusText_Reports_Item_Counts()
    {
        var fs = new FakeFileSystem();
        fs.Entries.AddRange([Dir("d1"), FileEntry("f1"), FileEntry("f2")]);

        var vm = await CreateAndLoadAsync(fs);

        Assert.That(vm.StatusText, Is.EqualTo("3 items (1 folders, 2 files)"));
    }
}
