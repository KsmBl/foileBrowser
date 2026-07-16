using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class BreadcrumbTests
{
    [Test]
    public async Task Breadcrumbs_Reflect_Current_Folder()
    {
        var fs = new FakeFileSystem { ParentOverride = null };
        var tab = new FileTabViewModel(fs);

        await tab.NavigateToAsync("/x/projects");

        Assert.That(tab.Breadcrumbs.Select(b => b.Name), Is.EqualTo(new[] { "projects" }));
        Assert.That(tab.Breadcrumbs[0].Path, Is.EqualTo("/x/projects"));
        Assert.That(tab.Breadcrumbs[0].ShowSeparator, Is.False, "the first segment has no leading chevron");
    }

    [Test]
    public async Task Breadcrumbs_Walk_Up_Through_Parents()
    {
        // A parent override of "/" makes the walk terminate at the root after one step.
        var fs = new FakeFileSystem { ParentOverride = "/" };
        var tab = new FileTabViewModel(fs);

        await tab.NavigateToAsync("/x");

        Assert.That(tab.Breadcrumbs.Select(b => b.Name), Is.EqualTo(new[] { "/", "x" }));
        Assert.That(tab.Breadcrumbs[1].ShowSeparator, Is.True, "non-root segments show a chevron");
    }

    [Test]
    public async Task NavigateBreadcrumb_Jumps_To_Segment()
    {
        var fs = new FakeFileSystem { ParentOverride = null };
        var tab = new FileTabViewModel(fs);
        await tab.NavigateToAsync("/x/projects");

        await tab.NavigateBreadcrumbCommand.ExecuteAsync(new BreadcrumbSegment("x", "/x"));

        Assert.That(tab.CurrentPath, Is.EqualTo("/x"));
    }
}
