using FoileBrowser.Services;

namespace FoileBrowser.Tests;

/// <summary>
/// What the path bar offers while a path is being typed (PRD §6.1). The filesystem is a lookup the
/// test supplies, so these are the ranking and matching rules rather than a tour of someone's disk.
/// </summary>
[TestFixture]
public class PathCompletionTests
{
    private static readonly Dictionary<string, string[]> Tree = new(StringComparer.Ordinal)
    {
        ["/"] = ["/home", "/etc", "/var"],
        ["/home"] = ["/home/hawky"],
        ["/home/hawky"] = ["/home/hawky/Documents", "/home/hawky/Downloads", "/home/hawky/Pictures"],
        ["/home/hawky/Documents"] = ["/home/hawky/Documents/Reports", "/home/hawky/Documents/Receipts"],
    };

    private static IEnumerable<string> Children(string directory)
        => Tree.TryGetValue(directory, out var children) ? children : [];

    private static IReadOnlyList<string> Complete(string typed, params string[] recents)
        => PathCompletion.Complete(typed, recents, Children);

    [Test]
    public void Nothing_Typed_Offers_Nothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Complete(""), Is.Empty);
            Assert.That(Complete("   "), Is.Empty);
        });
    }

    [Test]
    public void A_Trailing_Separator_Asks_For_Everything_Inside()
    {
        var result = Complete("/home/hawky/");

        Assert.That(result, Is.EqualTo(new[]
        {
            "/home/hawky/Documents",
            "/home/hawky/Downloads",
            "/home/hawky/Pictures",
        }));
    }

    [Test]
    public void A_Half_Typed_Segment_Completes_Against_Its_Parent()
    {
        Assert.That(Complete("/home/hawky/Do"), Is.EqualTo(new[]
        {
            "/home/hawky/Documents",
            "/home/hawky/Downloads",
        }));
    }

    [Test]
    public void Matching_Ignores_Case_Because_Typing_Shift_Is_Not_The_Point()
        => Assert.That(Complete("/home/hawky/doc"), Is.EqualTo(new[] { "/home/hawky/Documents" }));

    [Test]
    public void A_Segment_Hanging_Off_The_Root_Completes_Against_The_Root()
        => Assert.That(Complete("/et"), Is.EqualTo(new[] { "/etc" }));

    [Test]
    public void A_Recent_Folder_Matches_Anywhere_In_Its_Path_Not_Just_The_Start()
    {
        // The point of the recents list: nobody remembers which of six levels a folder hung off,
        // but they remember what it was called.
        var result = Complete("Receipts", "/home/hawky/Documents/Receipts");

        Assert.That(result, Does.Contain("/home/hawky/Documents/Receipts"));
    }

    [Test]
    public void Recent_Folders_Come_Before_What_The_Filesystem_Offers()
    {
        // Somewhere you have already been beats somewhere that merely exists.
        var result = Complete("/home/hawky/D", "/home/hawky/Downloads");

        Assert.That(result[0], Is.EqualTo("/home/hawky/Downloads"));
        Assert.That(result, Does.Contain("/home/hawky/Documents"));
    }

    [Test]
    public void A_Folder_Offered_By_Both_Sources_Is_Only_Offered_Once()
    {
        var result = Complete("/home/hawky/Doc", "/home/hawky/Documents");

        Assert.That(result.Count(p => p == "/home/hawky/Documents"), Is.EqualTo(1));
    }

    [Test]
    public void The_Folder_Already_Typed_Is_Not_Offered_Back()
    {
        // Suggesting exactly what is in the field is a row that does nothing.
        var result = Complete("/home/hawky/Documents", "/home/hawky/Documents");

        Assert.That(result, Does.Not.Contain("/home/hawky/Documents"));
    }

    [Test]
    public void An_Unreadable_Or_Nonexistent_Folder_Simply_Offers_Nothing()
    {
        // Half-typed paths point at folders that do not exist yet; that is the normal case, not an
        // error to surface.
        Assert.Multiple(() =>
        {
            Assert.That(Complete("/no/such/place/x"), Is.Empty);
            Assert.That(Complete("relative-with-no-separator"), Is.Empty);
        });
    }

    [Test]
    public void The_List_Is_Capped_So_A_Wide_Folder_Does_Not_Flood_The_Bar()
    {
        var wide = Enumerable.Range(0, 100).Select(i => $"/wide/dir{i:D3}").ToArray();
        var result = PathCompletion.Complete("/wide/", [], dir => dir == "/wide" ? wide : []);

        Assert.That(result, Has.Count.EqualTo(PathCompletion.Limit));
    }
}
