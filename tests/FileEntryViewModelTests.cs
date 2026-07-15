using FoileBrowser.Models;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class FileEntryViewModelTests
{
    [TestCase(0, "0 B")]
    [TestCase(512, "512 B")]
    [TestCase(1024, "1 KB")]
    [TestCase(1536, "1.5 KB")]
    [TestCase(1048576, "1 MB")]
    [TestCase(1073741824, "1 GB")]
    public void FormatSize_Is_Human_Readable(long bytes, string expected)
    {
        Assert.That(FileEntryViewModel.FormatSize(bytes), Is.EqualTo(expected));
    }

    [Test]
    public void TypeDisplay_Describes_Folders_And_Files()
    {
        var folder = new FileEntryViewModel(new FileSystemEntry
        {
            Name = "docs", FullPath = "/docs", Kind = FileSystemEntryKind.Directory,
        });
        var file = new FileEntryViewModel(new FileSystemEntry
        {
            Name = "report.pdf", FullPath = "/report.pdf", Kind = FileSystemEntryKind.File, Size = 10,
        });

        Assert.That(folder.TypeDisplay, Is.EqualTo("Folder"));
        Assert.That(file.TypeDisplay, Is.EqualTo("PDF file"));
    }

    [Test]
    public void SizeDisplay_Is_Empty_For_Directories()
    {
        var folder = new FileEntryViewModel(new FileSystemEntry
        {
            Name = "docs", FullPath = "/docs", Kind = FileSystemEntryKind.Directory,
        });

        Assert.That(folder.SizeDisplay, Is.Empty);
    }
}
