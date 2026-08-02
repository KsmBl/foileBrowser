using FoileBrowser.Models;
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
        TempTree.Remove(_root);
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

        // Joined the way the pane joins them: the toolkit's default separator is "/", which a Windows
        // drive root turns into "C:\/…".
        var bar = new Breadcrumb { PathSeparator = BreadcrumbSegment.Separator };
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

    // ---- the operations, against a real disk rather than a fake ----

    /// <summary>Auto-rename on any collision, the same resolver the transfer tests use.</summary>
    private static ConflictResolution Rename(ConflictRequest _) => ConflictResolution.Rename;

    [Test]
    public async Task A_File_Can_Be_Renamed_To_A_Name_With_An_Emoji_In_It()
    {
        var ops = new FileOperationService();
        var plain = Path.Combine(_root, "plain.txt");
        await File.WriteAllTextAsync(plain, "hello");

        var renamed = await ops.RenameAsync(plain, "🚀 launch notes.txt");

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(renamed), Is.EqualTo("🚀 launch notes.txt"));
            Assert.That(File.Exists(renamed), Is.True, "the name the OS got back is the name we asked for");
            Assert.That(File.Exists(plain), Is.False);
        });
    }

    [Test]
    public async Task An_Emoji_Named_File_Can_Be_Renamed_Back_To_Plain_Text()
    {
        var ops = new FileOperationService();
        var emoji = Path.Combine(_root, "🎉 party notes.txt");

        var renamed = await ops.RenameAsync(emoji, "sober notes.txt");

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(renamed), Is.EqualTo("sober notes.txt"));
            Assert.That(File.Exists(emoji), Is.False);
        });
    }

    [Test]
    public async Task Copying_Keeps_The_Name_Intact_Including_Into_An_Emoji_Named_Folder()
    {
        var ops = new FileOperationService();
        var destination = Path.Combine(_root, "📁 emoji folder");
        var source = Path.Combine(_root, "family 👩‍💻 photo.jpg");

        await ops.TransferAsync([source], destination, FileOperationKind.Copy, null, Rename);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(destination, "family 👩‍💻 photo.jpg")), Is.True);
            Assert.That(File.Exists(source), Is.True, "a copy leaves the original where it was");
        });
    }

    [Test]
    public async Task Moving_Carries_An_Emoji_Name_To_The_Destination()
    {
        var ops = new FileOperationService();
        var destination = Path.Combine(_root, "📁 emoji folder");
        var source = Path.Combine(_root, "flag 🇩🇪.txt");

        await ops.TransferAsync([source], destination, FileOperationKind.Move, null, Rename);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(destination, "flag 🇩🇪.txt")), Is.True);
            Assert.That(File.Exists(source), Is.False);
        });
    }

    [Test]
    public async Task A_Second_Copy_Of_An_Emoji_Name_Is_Disambiguated_Without_Mangling_It()
    {
        // The "keep both" name is built by taking the name apart and putting it back together, which
        // is the one place a copy could cut an emoji in half.
        var ops = new FileOperationService();
        var destination = Directory.CreateDirectory(Path.Combine(_root, "dest")).FullName;
        var source = Path.Combine(_root, "🎉 party notes.txt");

        await ops.TransferAsync([source], destination, FileOperationKind.Copy, null, Rename);
        await ops.TransferAsync([source], destination, FileOperationKind.Copy, null, Rename);

        var landed = Directory.GetFiles(destination).Select(Path.GetFileName).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(landed, Has.Count.EqualTo(2), "the second copy did not overwrite the first");
            Assert.That(landed, Does.Contain("🎉 party notes.txt"));
            foreach (var name in landed)
                Assert.That(name, Does.StartWith("🎉"), $"\"{name}\" lost the emoji it started with");
        });
    }
}
