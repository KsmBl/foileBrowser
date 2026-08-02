using FoileBrowser.Services;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Tests;

/// <summary>
/// What the path bar shows when it is clicked (PRD §6.1). Clicking the crumbs turns them into an
/// editable field seeded from the composed path, so the segments the view-model produces have to
/// compose back into the path they came from — a root segment is "/" and a naive join doubles it.
/// </summary>
[TestFixture]
public class PathBarTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-pathbar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Documents", "Reports"));
    }

    [TearDown]
    public void TearDown()
    {
        TempTree.Remove(_root);
    }

    /// <summary>
    /// Fills a breadcrumb the way the pane does, and asks it for the path it composes. The separator
    /// comes from the same place the pane takes it from, so this cannot pass while the real bar joins
    /// a Windows path with a POSIX separator.
    /// </summary>
    private static string Compose(IEnumerable<BreadcrumbSegment> segments)
    {
        var bar = new Breadcrumb { PathSeparator = BreadcrumbSegment.Separator };
        foreach (var segment in segments)
            bar.Items.Add(new BreadcrumbItem(segment.Name) { Tag = segment });

        return bar.FullPath;
    }

    [Test]
    public async Task The_Composed_Path_Is_The_Folder_It_Came_From()
    {
        var tab = new FileTabViewModel(new FileSystemService());
        var folder = Path.Combine(_root, "Documents", "Reports");

        await tab.NavigateToAsync(folder);

        Assert.That(Compose(tab.Breadcrumbs), Is.EqualTo(folder));
    }

    [Test]
    public async Task The_Filesystem_Root_Does_Not_Come_Back_Doubled()
    {
        var tab = new FileTabViewModel(new FileSystemService());
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        await tab.NavigateToAsync(root);

        var composed = Compose(tab.Breadcrumbs);
        Assert.That(composed, Is.EqualTo(root));
        Assert.That(composed, Does.Not.Contain(BreadcrumbSegment.Separator + BreadcrumbSegment.Separator),
            "the root crumb must not double the separator");
    }

    /// <summary>
    /// The three tests around this one compose whatever root the host happens to have, so a Windows
    /// drive root is invisible to a Linux runner — which is exactly how "C:\/$Recycle.Bin" reached CI
    /// and failed there alone. These drive the composition directly with each platform's shape.
    /// </summary>
    [TestCase(@"C:\", "Users", @"C:\", @"C:\Users")]
    [TestCase("/", "home", "/", "/home")]
    public void A_Root_Crumb_Carrying_Its_Separator_Does_Not_Get_A_Second_One(
        string root, string child, string separator, string expected)
    {
        var bar = new Breadcrumb { PathSeparator = separator };
        bar.Items.Add(new BreadcrumbItem(root));
        bar.Items.Add(new BreadcrumbItem(child));

        Assert.That(bar.FullPath, Is.EqualTo(expected));
    }

    [Test]
    public async Task A_Folder_Directly_Under_The_Root_Keeps_One_Separator()
    {
        var tab = new FileTabViewModel(new FileSystemService());
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var child = Directory.EnumerateDirectories(root).FirstOrDefault();
        if (child is null)
            Assert.Ignore("no directory directly under the filesystem root to walk into");

        await tab.NavigateToAsync(child!);

        Assert.That(Compose(tab.Breadcrumbs), Is.EqualTo(child));
    }
}
