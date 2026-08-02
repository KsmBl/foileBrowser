using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

/// <summary>
/// Values that arrive after a row is already on screen have to say so, or nothing redraws.
/// </summary>
/// <remarks>
/// A listing pulls each cell's text at paint time. <see cref="FileEntryViewModel.CellVersion"/>
/// recorded that a cell had changed and no view listened to it, so a metadata column read off the
/// file stayed blank until the folder was left and re-entered, and switching KiB/KB or the date
/// format changed nothing on a list already showing.
/// </remarks>
[TestFixture]
public class CellRefreshTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-cells-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "a.png"), ImageFixture.Encode(".png", 40, 24));
        File.WriteAllText(Path.Combine(_root, "n.txt"), new string('x', 4096));
    }

    [TearDown]
    public void TearDown() => TempTree.Remove(_root);

    [Test]
    public async Task Toggling_The_Display_Announces_That_The_Cells_Changed()
    {
        var display = new DisplayOptions();
        var tab = new FileTabViewModel(new FileSystemService(), display: display);
        await tab.NavigateToAsync(_root);

        var announced = 0;
        tab.CellsChanged += (_, _) => ++announced;

        display.SizeUnit = SizeUnit.Bytes;
        tab.RefreshDisplays();

        Assert.That(announced, Is.EqualTo(1), "one announcement for the whole listing, not one per row");
    }

    [Test]
    public async Task A_Metadata_Value_Announces_Itself_When_It_Arrives()
    {
        var tab = new FileTabViewModel(new FileSystemService(), metadata: new MetadataService());
        await tab.NavigateToAsync(_root);

        var arrived = new TaskCompletionSource();
        tab.CellsChanged += (_, _) => arrived.TrySetResult();

        var picture = tab.Entries.First(e => e.Name == "a.png");
        Assert.That(picture.GetCellText("img.dimensions"), Is.EqualTo("…"), "asked for, not yet known");

        await Task.WhenAny(arrived.Task, Task.Delay(5000));

        Assert.Multiple(() =>
        {
            Assert.That(arrived.Task.IsCompletedSuccessfully, Is.True, "the arrival was announced");
            Assert.That(picture.GetCellText("img.dimensions"), Is.EqualTo("40×24"));
        });
    }

    /// <summary>The text really does change with the unit, so the announcement is worth acting on.</summary>
    [Test]
    public async Task The_Size_Text_Follows_The_Unit()
    {
        var display = new DisplayOptions { SizeUnit = SizeUnit.Binary };
        var tab = new FileTabViewModel(new FileSystemService(), display: display);
        await tab.NavigateToAsync(_root);

        var notes = tab.Entries.First(e => e.Name == "n.txt");
        var binary = notes.GetCellText("size");

        display.SizeUnit = SizeUnit.Bytes;
        tab.RefreshDisplays();

        Assert.That(notes.GetCellText("size"), Is.Not.EqualTo(binary));
    }
}
