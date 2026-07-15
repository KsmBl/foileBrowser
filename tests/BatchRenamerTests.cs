using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class BatchRenamerTests
{
    private static FileSystemEntry Entry(string name, DateTimeOffset? modified = null) => new()
    {
        Name = name, FullPath = "/x/" + name, Kind = FileSystemEntryKind.File, Modified = modified,
    };

    [Test]
    public void Literal_Find_Replace()
    {
        var rule = new BatchRenameRule { Find = "IMG", Replace = "Photo" };
        var result = BatchRenamer.Preview([Entry("IMG_1.jpg"), Entry("IMG_2.jpg")], rule);

        Assert.That(result[0].ProposedName, Is.EqualTo("Photo_1.jpg"));
        Assert.That(result[1].ProposedName, Is.EqualTo("Photo_2.jpg"));
    }

    [Test]
    public void Template_Mode_With_Counter_And_Padding()
    {
        var rule = new BatchRenameRule { Find = "", Replace = "file-{n}{ext}", CounterStart = 1, CounterPadding = 3 };
        var result = BatchRenamer.Preview([Entry("a.txt"), Entry("b.txt")], rule);

        Assert.That(result[0].ProposedName, Is.EqualTo("file-001.txt"));
        Assert.That(result[1].ProposedName, Is.EqualTo("file-002.txt"));
    }

    [Test]
    public void Name_And_Ext_Tokens_Reproduce_Original()
    {
        var rule = new BatchRenameRule { Find = "", Replace = "{name}{ext}" };
        var result = BatchRenamer.Preview([Entry("report.md")], rule);

        Assert.That(result[0].ProposedName, Is.EqualTo("report.md"));
        Assert.That(result[0].Changed, Is.False);
    }

    [Test]
    public void Date_Token_Uses_Modified_Date()
    {
        var rule = new BatchRenameRule { Find = "", Replace = "{date}_{name}{ext}" };
        var when = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var result = BatchRenamer.Preview([Entry("x.txt", when)], rule);

        Assert.That(result[0].ProposedName, Does.StartWith("2026-07-15_"));
    }

    [Test]
    public void Regex_Groups_Are_Supported()
    {
        var rule = new BatchRenameRule { Find = @"(\d+)", Replace = "#$1", UseRegex = true };
        var result = BatchRenamer.Preview([Entry("track09.flac")], rule);

        Assert.That(result[0].ProposedName, Is.EqualTo("track#09.flac"));
    }

    [Test]
    public void Colliding_Names_Are_Disambiguated()
    {
        var rule = new BatchRenameRule { Find = "", Replace = "same.txt" };
        var result = BatchRenamer.Preview([Entry("a.txt"), Entry("b.txt")], rule);

        Assert.That(result[0].ProposedName, Is.EqualTo("same.txt"));
        Assert.That(result[1].ProposedName, Is.EqualTo("same (2).txt"));
    }

    [Test]
    public void Invalid_Regex_Throws()
    {
        var rule = new BatchRenameRule { Find = "(", Replace = "x", UseRegex = true };
        Assert.Throws<System.Text.RegularExpressions.RegexParseException>(
            () => BatchRenamer.Preview([Entry("a.txt")], rule));
    }
}
