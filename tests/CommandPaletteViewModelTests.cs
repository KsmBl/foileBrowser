using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class CommandPaletteViewModelTests
{
    private static CommandPaletteViewModel Palette(params string[] titles)
    {
        var commands = titles.Select(t => new CommandItem(t, t, "Test", null, () => Task.CompletedTask));
        return new CommandPaletteViewModel(commands);
    }

    [Test]
    public void Empty_Query_Lists_All_Commands()
    {
        var palette = Palette("New Folder", "Delete", "Rename");
        Assert.That(palette.Results, Has.Count.EqualTo(3));
        Assert.That(palette.Selected, Is.Not.Null);
    }

    [Test]
    public void Query_Fuzzy_Filters_And_Ranks()
    {
        var palette = Palette("New Folder", "New File", "Delete");
        palette.Query = "nf";

        Assert.That(palette.Results.Select(r => r.Title), Does.Contain("New Folder"));
        Assert.That(palette.Results.Select(r => r.Title), Does.Not.Contain("Delete"));
    }

    [Test]
    public void MoveSelection_Is_Clamped()
    {
        var palette = Palette("A", "B", "C");

        palette.MoveSelection(-5);
        Assert.That(palette.Selected!.Title, Is.EqualTo("A"));

        palette.MoveSelection(99);
        Assert.That(palette.Selected!.Title, Is.EqualTo("C"));
    }

    [Test]
    public async Task ExecuteSelected_Runs_Command_And_Closes()
    {
        var ran = false;
        var palette = new CommandPaletteViewModel([new CommandItem("x", "Run", "Test", null, () => { ran = true; return Task.CompletedTask; })]);
        palette.Open();

        await palette.ExecuteSelectedCommand.ExecuteAsync(null);

        Assert.That(ran, Is.True);
        Assert.That(palette.IsOpen, Is.False);
    }

    [Test]
    public void Open_Resets_Query_And_Shows()
    {
        var palette = Palette("A", "B");
        palette.Query = "z"; // filters everything out

        palette.Open();

        Assert.That(palette.IsOpen, Is.True);
        Assert.That(palette.Query, Is.Empty);
        Assert.That(palette.Results, Has.Count.EqualTo(2));
    }
}
