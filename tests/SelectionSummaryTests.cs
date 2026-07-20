using FoileBrowser.Models;

namespace FoileBrowser.Tests;

[TestFixture]
public class SelectionSummaryTests
{
    private static FileSystemEntry File(string name, long size, DateTimeOffset? modified = null) => new()
    {
        Name = name,
        FullPath = "/demo/" + name,
        Kind = FileSystemEntryKind.File,
        Size = size,
        Modified = modified,
    };

    private static FileSystemEntry Folder(string name) => new()
    {
        Name = name,
        FullPath = "/demo/" + name,
        Kind = FileSystemEntryKind.Directory,
    };

    private static PreviewResult Build(params FileSystemEntry[] entries) =>
        SelectionSummary.Build(entries, SizeUnit.Bytes, DateDisplay.Absolute);

    [Test]
    public void Totals_And_Averages_The_Selected_Files()
    {
        var result = Build(File("a.txt", 100), File("b.txt", 300));

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.EqualTo("2 items selected"));
            Assert.That(result.Text, Does.Contain("Items          2"));
            Assert.That(result.Text, Does.Contain("400 bytes"), "total size");
            Assert.That(result.Text, Does.Contain("Average size   200"));
            Assert.That(result.Text, Does.Contain("Largest").And.Contain("b.txt"));
            Assert.That(result.Text, Does.Contain("Smallest").And.Contain("a.txt"));
        });
    }

    [Test]
    public void Counts_Folders_Separately_And_Excludes_Them_From_Sizes()
    {
        var result = Build(File("a.txt", 100), Folder("docs"), Folder("pics"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("(1 file(s), 2 folder(s))"));
            Assert.That(result.Text, Does.Contain("100 bytes"), "only the file counts toward the total");
            Assert.That(result.Text, Does.Contain("Folder contents aren't counted"),
                "the exclusion is stated rather than left implicit");
        });
    }

    [Test]
    public void Breaks_The_Selection_Down_By_File_Type_Largest_First()
    {
        var result = Build(
            File("a.txt", 10), File("b.txt", 20),
            File("photo.png", 5000),
            File("readme", 1));

        Assert.That(result.Text, Is.Not.Null);
        var text = result.Text!;
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("File types"));
            Assert.That(text, Does.Contain("PNG"));
            Assert.That(text, Does.Contain("TXT"));
            Assert.That(text, Does.Contain("(no extension)"));
            Assert.That(text.IndexOf("PNG", StringComparison.Ordinal),
                Is.LessThan(text.IndexOf("TXT", StringComparison.Ordinal)),
                "the largest type is listed first");
        });
    }

    [Test]
    public void Shows_The_Modified_Range_As_A_Span_Or_A_Single_Date()
    {
        var older = new DateTimeOffset(2020, 1, 2, 3, 4, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2024, 5, 6, 7, 8, 0, TimeSpan.Zero);

        var span = Build(File("a.txt", 1, older), File("b.txt", 1, newer));
        var same = Build(File("a.txt", 1, older), File("b.txt", 1, older));

        Assert.Multiple(() =>
        {
            Assert.That(span.Text, Does.Contain("→"), "a range is shown as a span");
            Assert.That(same.Text, Does.Not.Contain("→"), "identical dates collapse to one");
        });
    }

    [Test]
    public void Summarises_A_Selection_Of_Folders_Only_Without_Size_Figures()
    {
        var result = Build(Folder("one"), Folder("two"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("(0 file(s), 2 folder(s))"));
            Assert.That(result.Text, Does.Not.Contain("Average size"));
            Assert.That(result.Info, Does.Contain("2 folder(s)"));
        });
    }

    [Test]
    public void Collapses_A_Long_Tail_Of_Types_Into_One_Row()
    {
        var entries = Enumerable.Range(0, 20).Select(i => File($"f{i}.t{i}", 10)).ToArray();

        var result = Build(entries);

        Assert.That(result.Text, Does.Contain("other type(s)"));
    }
}
