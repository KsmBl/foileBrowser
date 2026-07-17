using FoileBrowser.Models;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class SelectionStatusTests
{
    private static FileEntryViewModel File(string name, long size) =>
        new(new FileSystemEntry { Name = name, FullPath = "/x/" + name, Kind = FileSystemEntryKind.File, Size = size });

    [Test]
    public void SetSelection_Summarises_Count_And_Total_Size()
    {
        var tab = new FileTabViewModel(new FakeFileSystem());

        tab.SetSelection([File("a.bin", 1024), File("b.bin", 1024)]);

        Assert.That(tab.SelectionStatus, Does.StartWith("2 selected"));
        Assert.That(tab.SelectionStatus, Does.Contain("2 KiB"));
        Assert.That(tab.StatusLine, Is.EqualTo(tab.SelectionStatus), "the status line shows the selection when present");
    }

    [Test]
    public void SetSelection_Empty_Clears_Summary_And_Falls_Back_To_Folder_Status()
    {
        var tab = new FileTabViewModel(new FakeFileSystem()) { StatusText = "5 items" };

        tab.SetSelection([File("a", 1)]);
        tab.SetSelection([]);

        Assert.That(tab.SelectionStatus, Is.Empty);
        Assert.That(tab.StatusLine, Is.EqualTo("5 items"));
    }
}
