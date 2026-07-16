using FoileBrowser.Models;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class FileEntryViewModelTests
{
    [TestCase(0, "0 B")]
    [TestCase(512, "512 B")]
    [TestCase(1024, "1 KiB")]
    [TestCase(1536, "1.5 KiB")]
    [TestCase(1048576, "1 MiB")]
    [TestCase(1073741824, "1 GiB")]
    public void Size_Binary_Uses_IEC_Units(long bytes, string expected)
        => Assert.That(ValueFormat.Size(bytes, SizeUnit.Binary), Is.EqualTo(expected));

    [TestCase(0, "0 B")]
    [TestCase(1000, "1 KB")]
    [TestCase(1500000, "1.5 MB")]
    public void Size_Decimal_Uses_SI_Units(long bytes, string expected)
        => Assert.That(ValueFormat.Size(bytes, SizeUnit.Decimal), Is.EqualTo(expected));

    [TestCase(0, "0 B")]
    [TestCase(1234567, "1,234,567 B")]
    public void Size_Bytes_Is_Exact_And_Grouped(long bytes, string expected)
        => Assert.That(ValueFormat.Size(bytes, SizeUnit.Bytes), Is.EqualTo(expected));

    [Test]
    public void Date_Relative_Reads_Naturally()
    {
        var now = new DateTimeOffset(2026, 07, 16, 12, 0, 0, TimeSpan.Zero);
        string Rel(TimeSpan ago) => ValueFormat.Date(now - ago, DateDisplay.Relative, now);

        Assert.That(Rel(TimeSpan.FromSeconds(10)), Is.EqualTo("just now"));
        Assert.That(Rel(TimeSpan.FromMinutes(5)), Is.EqualTo("5 min ago"));
        Assert.That(Rel(TimeSpan.FromHours(3)), Is.EqualTo("3 h ago"));
        Assert.That(Rel(TimeSpan.FromDays(1)), Is.EqualTo("yesterday"));
        Assert.That(Rel(TimeSpan.FromDays(3)), Is.EqualTo("3 days ago"));
        Assert.That(Rel(TimeSpan.FromDays(20)), Is.EqualTo("2 weeks ago"));
        Assert.That(Rel(TimeSpan.FromDays(60)), Is.EqualTo("2 months ago"));
        Assert.That(Rel(TimeSpan.FromDays(400)), Is.EqualTo("1 year ago"));
    }

    [Test]
    public void Date_Absolute_Is_A_Timestamp()
    {
        var dt = new DateTimeOffset(2026, 07, 16, 8, 16, 0, TimeSpan.Zero);
        Assert.That(ValueFormat.Date(dt, DateDisplay.Absolute),
            Is.EqualTo(dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")));
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
