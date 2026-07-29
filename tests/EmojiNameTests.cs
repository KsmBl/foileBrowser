using FoileBrowser.Services;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Tests;

/// <summary>
/// Names that are not plain ASCII survive the trip from disk to what a view shows. Rendering them is
/// the toolkit's business — GTK falls back through Pango, Win32 routes colour glyphs to DirectWrite —
/// but everything up to the draw call is ours, and an emoji is where the seams show: it is a surrogate
/// pair, so anything that treats a name as a sequence of chars can cut one in half and produce a name
/// that is not the file's.
/// </summary>
[TestFixture]
public class EmojiNameTests
{
    private static readonly string[] Names =
    [
        "🎉 party notes.txt",       // leading emoji, supplementary plane
        "vacation 🏖️.md",           // emoji with a variation selector
        "café ☕ résumé.pdf",        // BMP emoji between accented Latin
        "日本語 テスト.txt",          // no emoji, but not Latin either
        "family 👩‍💻 photo.jpg",     // a zero-width-joiner sequence
        "flag 🇩🇪.txt",              // two regional indicators
    ];

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-emoji-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        foreach (var name in Names)
            File.WriteAllText(Path.Combine(_root, name), "x");

        Directory.CreateDirectory(Path.Combine(_root, "📁 emoji folder"));
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Test]
    public async Task A_Listing_Shows_Each_Name_Exactly_As_It_Is_On_Disk()
    {
        var tab = new FileTabViewModel(new FileSystemService());

        await tab.NavigateToAsync(_root);

        var listed = tab.Entries.Select(e => e.Name).ToList();
        foreach (var name in Names)
            Assert.That(listed, Does.Contain(name));
    }

    [Test]
    public async Task A_Crumb_Bar_Rooted_In_An_Emoji_Folder_Composes_Its_Real_Path()
    {
        var tab = new FileTabViewModel(new FileSystemService());
        var folder = Path.Combine(_root, "📁 emoji folder");

        await tab.NavigateToAsync(folder);

        var bar = new Breadcrumb();
        foreach (var segment in tab.Breadcrumbs)
            bar.Items.Add(new BreadcrumbItem(segment.Name) { Tag = segment });

        Assert.That(bar.FullPath, Is.EqualTo(folder));
    }

    [Test]
    public async Task Filtering_Matches_On_An_Emoji_The_User_Typed()
    {
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);

        tab.FilterText = "🎉";

        Assert.That(tab.Entries.Select(e => e.Name), Is.EqualTo(new[] { "🎉 party notes.txt" }));
    }

    [Test]
    public async Task Type_Ahead_Finds_A_Name_That_Starts_With_An_Emoji()
    {
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);

        // The keystroke is a surrogate pair; a match that compared one char at a time would miss it.
        var match = tab.Entries.FirstOrDefault(e => e.Name.StartsWith("🎉", StringComparison.Ordinal));

        Assert.That(match?.Name, Is.EqualTo("🎉 party notes.txt"));
    }
}
