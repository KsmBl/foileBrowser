using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class EntrySorterTests
{
    private static FileSystemEntry File(string name, long size = 0, DateTimeOffset? modified = null) => new()
    {
        Name = name,
        FullPath = "/root/" + name,
        Kind = FileSystemEntryKind.File,
        Size = size,
        Modified = modified,
    };

    private static FileSystemEntry Dir(string name, DateTimeOffset? modified = null) => new()
    {
        Name = name,
        FullPath = "/root/" + name,
        Kind = FileSystemEntryKind.Directory,
        Modified = modified,
    };

    [Test]
    public void Directories_Are_Grouped_Before_Files_Ascending()
    {
        var input = new[] { File("b.txt"), Dir("zeta"), File("a.txt"), Dir("alpha") };

        var sorted = EntrySorter.Sort(input, SortColumn.Name, SortDirection.Ascending);

        Assert.That(sorted.Select(e => e.Name), Is.EqualTo(new[] { "alpha", "zeta", "a.txt", "b.txt" }));
    }

    [Test]
    public void Directories_Stay_Grouped_First_Even_When_Descending()
    {
        var input = new[] { File("b.txt"), Dir("alpha"), File("a.txt"), Dir("zeta") };

        var sorted = EntrySorter.Sort(input, SortColumn.Name, SortDirection.Descending);

        // Folders remain ahead of files; within each group the name order reverses.
        Assert.That(sorted.Select(e => e.Name), Is.EqualTo(new[] { "zeta", "alpha", "b.txt", "a.txt" }));
    }

    [Test]
    public void Sort_By_Size_Ascending_Orders_Files_Smallest_First()
    {
        var input = new[] { File("big", 5000), File("small", 10), File("mid", 900) };

        var sorted = EntrySorter.Sort(input, SortColumn.Size, SortDirection.Ascending);

        Assert.That(sorted.Select(e => e.Name), Is.EqualTo(new[] { "small", "mid", "big" }));
    }

    [Test]
    public void Sort_By_Modified_Descending_Orders_Newest_First()
    {
        var old = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        var mid = DateTimeOffset.Parse("2023-06-15T00:00:00Z");
        var recent = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        var input = new[] { File("a", modified: old), File("b", modified: recent), File("c", modified: mid) };

        var sorted = EntrySorter.Sort(input, SortColumn.Modified, SortDirection.Descending);

        Assert.That(sorted.Select(e => e.Name), Is.EqualTo(new[] { "b", "c", "a" }));
    }

    [Test]
    public void Sort_By_Type_Groups_By_Extension()
    {
        var input = new[] { File("readme.md"), File("photo.png"), File("notes.md"), File("icon.png") };

        var sorted = EntrySorter.Sort(input, SortColumn.Type, SortDirection.Ascending);

        // Same extension groups together; name is the tie-breaker within a group.
        Assert.That(sorted.Select(e => e.Name), Is.EqualTo(new[] { "notes.md", "readme.md", "icon.png", "photo.png" }));
    }

    [Test]
    public void Sort_Is_Stable_On_Equal_Keys_Via_Name_Tiebreak()
    {
        var input = new[] { File("c", 100), File("a", 100), File("b", 100) };

        var sorted = EntrySorter.Sort(input, SortColumn.Size, SortDirection.Ascending);

        Assert.That(sorted.Select(e => e.Name), Is.EqualTo(new[] { "a", "b", "c" }));
    }
}
