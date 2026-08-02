using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

/// <summary>
/// The things the browser has to get right every time, against a real directory tree rather than a
/// fake: list a folder, go into one, come back out, select something, see it previewed.
/// </summary>
/// <remarks>
/// The per-piece tests elsewhere each prove one part. These exist so that a change to any of them
/// cannot quietly break the walk a person actually does — which is how Return came to step out of a
/// folder rather than into it, with every individual command still working perfectly.
/// </remarks>
[TestFixture]
public class BasicsRegressionTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-basics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Album"));
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "hello");
        File.WriteAllBytes(Path.Combine(_root, "Album", "one.png"), ImageFixture.Encode(".png", 8, 8));
        File.WriteAllBytes(Path.Combine(_root, "Album", "two.png"), ImageFixture.Encode(".png", 8, 8));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static FileTabViewModel Tab() => new(new FileSystemService());

    [Test]
    public async Task A_Folder_Lists_What_Is_In_It()
    {
        var tab = Tab();
        await tab.NavigateToAsync(_root);

        Assert.That(
            tab.Entries.Select(e => e.Name),
            Is.EquivalentTo(new[] { "Album", "Empty", "notes.txt" }));
    }

    [Test]
    public async Task Going_Into_A_Folder_And_Back_Out_Returns_Where_It_Started()
    {
        var tab = Tab();
        await tab.NavigateToAsync(_root);

        var album = tab.Entries.First(e => e.Name == "Album");
        await tab.OpenCommand.ExecuteAsync(album);
        Assert.That(tab.CurrentPath, Is.EqualTo(Path.Combine(_root, "Album")), "opening a folder goes into it");
        Assert.That(tab.Entries, Has.Count.EqualTo(2));

        await tab.GoUpCommand.ExecuteAsync(null);
        Assert.That(tab.CurrentPath, Is.EqualTo(_root), "and up comes back out");
    }

    [Test]
    public async Task History_Walks_Back_And_Forward()
    {
        var tab = Tab();
        await tab.NavigateToAsync(_root);
        await tab.NavigateToAsync(Path.Combine(_root, "Album"));

        Assert.That(tab.CanGoBack, Is.True);
        await tab.GoBackCommand.ExecuteAsync(null);
        Assert.That(tab.CurrentPath, Is.EqualTo(_root));

        await tab.GoForwardCommand.ExecuteAsync(null);
        Assert.That(tab.CurrentPath, Is.EqualTo(Path.Combine(_root, "Album")));
    }

    [Test]
    public async Task An_Empty_Folder_Lists_Nothing_And_Still_Works()
    {
        var tab = Tab();
        await tab.NavigateToAsync(Path.Combine(_root, "Empty"));

        Assert.That(tab.Entries, Is.Empty);
        await tab.GoUpCommand.ExecuteAsync(null);
        Assert.That(tab.CurrentPath, Is.EqualTo(_root));
    }

    [Test]
    public async Task A_Text_File_Previews_As_Its_Text()
    {
        var entry = await new FileSystemService().ListDirectoryAsync(_root);
        var notes = entry.First(e => e.Name == "notes.txt");

        var preview = await new PreviewService().CreateAsync(notes);

        Assert.Multiple(() =>
        {
            Assert.That(preview.Kind, Is.EqualTo(PreviewKind.Text));
            Assert.That(preview.Text, Does.Contain("hello"));
        });
    }

    [Test]
    public async Task An_Image_Previews_As_A_Picture()
    {
        var entries = await new FileSystemService().ListDirectoryAsync(Path.Combine(_root, "Album"));
        var picture = entries.First(e => e.Name == "one.png");

        var preview = await new PreviewService().CreateAsync(picture);

        Assert.Multiple(() =>
        {
            Assert.That(preview.Kind, Is.EqualTo(PreviewKind.Image));
            Assert.That(preview.HasImage, Is.True);
            Assert.That(preview.ImagePath, Does.EndWith("one.png"));
        });
    }

    /// <summary>A folder of pictures offers them all, which is what the filmstrip steps through.</summary>
    [Test]
    public async Task A_Folder_Of_Pictures_Offers_Every_Picture()
    {
        var fs = new FileSystemService();
        var entries = await fs.ListDirectoryAsync(_root);
        var album = entries.First(e => e.Name == "Album");

        var images = await SelectionImages.CollectAsync([album], fs.ListDirectoryAsync);

        Assert.That(
            images.Paths.Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "one.png", "two.png" }));
    }

    [Test]
    public async Task Filtering_Narrows_Without_Navigating()
    {
        var tab = Tab();
        await tab.NavigateToAsync(_root);

        tab.FilterText = "notes";

        Assert.Multiple(() =>
        {
            Assert.That(tab.Entries.Select(e => e.Name), Is.EqualTo(new[] { "notes.txt" }));
            Assert.That(tab.CurrentPath, Is.EqualTo(_root), "filtering is not navigation");
        });
    }
}
