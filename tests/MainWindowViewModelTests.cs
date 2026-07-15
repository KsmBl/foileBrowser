using FoileBrowser.Models;
using FoileBrowser.ViewModels;
using static FoileBrowser.Tests.FakeEntries;

namespace FoileBrowser.Tests;

/// <summary>Browsing behaviour now lives on <see cref="FileTabViewModel"/>.</summary>
[TestFixture]
public class FileTabViewModelTests
{
    private static async Task<FileTabViewModel> CreateAndLoadAsync(FakeFileSystem fs)
    {
        var tab = new FileTabViewModel(fs);
        await tab.NavigateToAsync("/x");
        return tab;
    }

    [Test]
    public async Task Navigate_Populates_Entries_Sorted_Folders_First()
    {
        var fs = new FakeFileSystem();
        fs.Entries.AddRange([File("b.txt"), Dir("zeta"), File("a.txt"), Dir("alpha")]);

        var tab = await CreateAndLoadAsync(fs);

        Assert.That(tab.Entries.Select(e => e.Name), Is.EqualTo(new[] { "alpha", "zeta", "a.txt", "b.txt" }));
        Assert.That(tab.CurrentPath, Is.EqualTo("/x"));
    }

    [Test]
    public async Task Hidden_Entries_Are_Filtered_Until_Toggled()
    {
        var fs = new FakeFileSystem();
        fs.Entries.AddRange([File("visible.txt"), File(".secret", hidden: true)]);

        var tab = await CreateAndLoadAsync(fs);
        Assert.That(tab.Entries.Select(e => e.Name), Does.Not.Contain(".secret"));

        tab.ShowHidden = true;
        Assert.That(tab.Entries.Select(e => e.Name), Does.Contain(".secret"));
    }

    [Test]
    public async Task SortByCommand_Toggles_Direction_On_Repeat()
    {
        var fs = new FakeFileSystem();
        fs.Entries.AddRange([Dir("alpha"), Dir("zeta")]);
        var tab = await CreateAndLoadAsync(fs);

        tab.SortByCommand.Execute(SortColumn.Size);
        tab.SortByCommand.Execute(SortColumn.Name);
        Assert.That(tab.SortDirection, Is.EqualTo(SortDirection.Ascending));
        Assert.That(tab.Entries.Select(e => e.Name), Is.EqualTo(new[] { "alpha", "zeta" }));

        tab.SortByCommand.Execute(SortColumn.Name);
        Assert.That(tab.SortDirection, Is.EqualTo(SortDirection.Descending));
        Assert.That(tab.Entries.Select(e => e.Name), Is.EqualTo(new[] { "zeta", "alpha" }));
    }

    [Test]
    public async Task Navigation_History_Enables_Back_After_Second_Navigation()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(Dir("child"));
        var tab = await CreateAndLoadAsync(fs);

        Assert.That(tab.CanGoBack, Is.False);

        await tab.NavigateToAsync("/x/child");
        Assert.That(tab.CanGoBack, Is.True);

        await tab.GoBackCommand.ExecuteAsync(null);
        Assert.That(tab.CurrentPath, Is.EqualTo("/x"));
    }

    [Test]
    public async Task CanGoUp_Reflects_Parent_Availability()
    {
        var fs = new FakeFileSystem { ParentOverride = "/parent" };
        var tab = await CreateAndLoadAsync(fs);
        Assert.That(tab.CanGoUp, Is.True);

        fs.ParentOverride = null;
        await tab.NavigateToAsync("/root");
        Assert.That(tab.CanGoUp, Is.False);
    }

    [Test]
    public async Task StatusText_Reports_Item_Counts()
    {
        var fs = new FakeFileSystem();
        fs.Entries.AddRange([Dir("d1"), File("f1"), File("f2")]);

        var tab = await CreateAndLoadAsync(fs);

        Assert.That(tab.StatusText, Is.EqualTo("3 items (1 folders, 2 files)"));
    }

    [Test]
    public async Task Title_Is_Current_Folder_Name()
    {
        var fs = new FakeFileSystem();
        var tab = new FileTabViewModel(fs);
        await tab.NavigateToAsync("/x/documents");

        Assert.That(tab.Title, Is.EqualTo("documents"));
    }
}
