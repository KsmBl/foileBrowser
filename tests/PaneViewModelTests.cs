using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class PaneViewModelTests
{
    [Test]
    public void AddTab_Activates_The_New_Tab()
    {
        var pane = new PaneViewModel(new FakeFileSystem());

        var first = pane.AddTab();
        var second = pane.AddTab();

        Assert.That(pane.Tabs, Has.Count.EqualTo(2));
        Assert.That(pane.ActiveTab, Is.SameAs(second));
        Assert.That(first, Is.Not.SameAs(second));
    }

    [Test]
    public void CloseTab_Keeps_At_Least_One_Tab()
    {
        var pane = new PaneViewModel(new FakeFileSystem());
        var only = pane.AddTab();

        pane.CloseTabCommand.Execute(only);

        Assert.That(pane.Tabs, Has.Count.EqualTo(1), "the last tab cannot be closed");
    }

    [Test]
    public void CloseTab_Selects_A_Neighbour()
    {
        var pane = new PaneViewModel(new FakeFileSystem());
        var a = pane.AddTab();
        var b = pane.AddTab();

        pane.ActiveTab = b;
        pane.CloseTabCommand.Execute(b);

        Assert.That(pane.Tabs, Has.Count.EqualTo(1));
        Assert.That(pane.ActiveTab, Is.SameAs(a));
    }

    [Test]
    public void Activate_Raises_Activated_Event()
    {
        var pane = new PaneViewModel(new FakeFileSystem());
        var raised = false;
        pane.Activated += (_, _) => raised = true;

        pane.ActivateCommand.Execute(null);

        Assert.That(raised, Is.True);
    }
}
