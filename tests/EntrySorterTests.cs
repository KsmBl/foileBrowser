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
    // ---- folders under a size sort ---------------------------------------------------------------

    /// <summary>
    /// Ordering by size orders the folders too, once their sizes have been counted.
    /// </summary>
    /// <remarks>
    /// A folder has no size of its own on disk, so every folder compared equal and the whole group
    /// fell through to the name tie-breaker: sorting by size left the folders in alphabetical order,
    /// which looks exactly like a sort that ignored them. The counted size is what they are ordered
    /// by, and it is the caller that knows it.
    /// </remarks>
    [Test]
    public void Ordering_By_Size_Orders_The_Folders_By_Their_Counted_Size()
    {
        var counted = new Dictionary<string, long> { ["/root/small"] = 10, ["/root/big"] = 9000, ["/root/mid"] = 500 };
        var input = new[] { Dir("small"), Dir("big"), Dir("mid"), File("f.txt", 42) };

        var sorted = EntrySorter.Sort(
            input, SortColumn.Size, SortDirection.Descending,
            e => counted.TryGetValue(e.FullPath, out var size) ? size : null);

        Assert.That(
            sorted.Select(e => e.Name),
            Is.EqualTo(new[] { "big", "mid", "small", "f.txt" }),
            "largest folder first, and folders still lead the files");
    }

    [Test]
    public void An_Uncounted_Folder_Falls_Back_To_Its_Name()
    {
        var counted = new Dictionary<string, long> { ["/root/known"] = 100 };
        var input = new[] { Dir("zeta"), Dir("known"), Dir("alpha") };

        var sorted = EntrySorter.Sort(
            input, SortColumn.Size, SortDirection.Descending,
            e => counted.TryGetValue(e.FullPath, out var size) ? size : null);

        Assert.That(
            sorted.Select(e => e.Name),
            Is.EqualTo(new[] { "known", "alpha", "zeta" }),
            "the one with a size sorts by it; the rest keep their alphabetical order");
    }

    /// <summary>With no lookup at all — the plain call — nothing about files changes.</summary>
    [Test]
    public void Files_Still_Sort_By_Size_Without_A_Folder_Lookup()
    {
        var input = new[] { File("small.txt", 1), File("big.txt", 100), File("mid.txt", 50) };

        var sorted = EntrySorter.Sort(input, SortColumn.Size, SortDirection.Descending);

        Assert.That(sorted.Select(e => e.Name), Is.EqualTo(new[] { "big.txt", "mid.txt", "small.txt" }));
    }
}
